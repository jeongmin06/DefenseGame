using DefenseGame.Server.Actors.Players;
using DefenseGame.Server.Actors.Runtime;
using DefenseGame.Server.Authentication;
using DefenseGame.Server.Contracts;
using DefenseGame.Server.Hosting;
using DefenseGame.Server.Services;
using DefenseGame.Server.Stores;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
    options.UseUtcTimestamp = true;
});

builder.Services.AddSingleton(sp => GuestAuthenticationOptions.FromConfiguration(
    sp.GetRequiredService<IConfiguration>()));
builder.Services.AddSingleton<GuestTokenService>();
builder.Services.AddSingleton<ISystemClock, SystemClock>();
builder.Services.AddSingleton<IProgressStore>(sp =>
{
    var configuredPath = builder.Configuration["ProgressStore:FilePath"];
    var filePath = string.IsNullOrWhiteSpace(configuredPath)
        ? Path.Combine(AppContext.BaseDirectory, "data", "progress.json")
        : configuredPath;

    var clock = sp.GetRequiredService<ISystemClock>();
    return new JsonFileProgressStore(filePath, clock);
});
builder.Services.AddSingleton<IStageProgressService, StageProgressService>();
builder.Services.AddSingleton<IStageSquadService, StageSquadService>();
builder.Services.AddSingleton<ISkillProfileService, SkillProfileService>();

builder.Services.AddSingleton<ActorThreadScheduler>();
builder.Services.AddSingleton<ActorThreadPool>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ActorThreadPool>());
builder.Services.AddSingleton<PlayerActorRegistry>();

var app = builder.Build();
_ = app.Services.GetRequiredService<GuestAuthenticationOptions>();

app.UseMiddleware<RequestObservationMiddleware>();
app.UseMiddleware<GuestAuthenticationMiddleware>();

app.MapGet("/health/live", () => Results.Ok(new { status = "ok", check = "live" }));
app.MapGet("/health/ready", (ActorThreadPool pool) => ReadinessResult(pool));
app.MapGet("/health", (ActorThreadPool pool) => ReadinessResult(pool));

app.MapPost("/v1/auth/guest", (GuestAuthenticationOptions options, GuestTokenService tokens) =>
{
    if (!options.CanIssueTokens)
    {
        return Results.Json(
            new
            {
                errorCode = "authentication_not_configured",
                message = "Guest token signing is not configured."
            },
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    string playerId = $"guest_{Guid.NewGuid():N}";
    GuestTokenIssueResult token = tokens.Issue(playerId);
    return Results.Ok(new GuestLoginResponse(token.PlayerId, token.AccessToken, token.ExpiresAtUtc));
});

app.MapPost("/v1/auth/refresh", (HttpContext context, GuestTokenService tokens) =>
{
    string? playerId = context.Items[GuestAuthenticationMiddleware.AuthenticatedPlayerIdItemKey] as string;
    if (string.IsNullOrEmpty(playerId))
    {
        return Results.Json(
            new { errorCode = "missing_access_token", message = "Bearer access token is required." },
            statusCode: StatusCodes.Status401Unauthorized);
    }

    GuestTokenIssueResult token = tokens.Issue(playerId);
    return Results.Ok(new GuestLoginResponse(token.PlayerId, token.AccessToken, token.ExpiresAtUtc));
});

RouteGroupBuilder playerApi = app.MapGroup("/v1");
playerApi.AddEndpointFilter<GuestPlayerAuthorizationFilter>();
playerApi.AddEndpointFilter<PlayerActorObservationFilter>();

playerApi.MapGet("/progress/{userId}", async (
    string userId,
    PlayerActorRegistry players,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(userId))
    {
        return Results.BadRequest(new { errorCode = "invalid_user_id", message = "userId is required." });
    }

    var progress = await players.Get(userId).GetProgressAsync(cancellationToken);
    return Results.Ok(progress);
});

playerApi.MapPost("/stage-clear", async (
    StageClearRequest request,
    PlayerActorRegistry players,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.UserId))
    {
        return Results.BadRequest(new { errorCode = "invalid_user_id", message = "userId is required." });
    }
    var result = await players.Get(request.UserId).HandleStageClearAsync(request, cancellationToken);
    return result.IsAccepted
        ? Results.Ok(result.Payload)
        : Results.BadRequest(new { errorCode = result.ErrorCode, message = result.ErrorMessage });
});

playerApi.MapGet("/squads/{userId}/{stageId}", async (
    string userId,
    string stageId,
    PlayerActorRegistry players,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(userId))
        return Results.BadRequest(new { errorCode = "invalid_user_id", message = "userId is required." });
    StageSquadHandleResult result = await players.Get(userId).GetStageSquadAsync(stageId, cancellationToken);
    return result.Status == StageSquadHandleStatus.Accepted
        ? Results.Ok(result.Payload)
        : Results.BadRequest(new { errorCode = result.ErrorCode, message = result.ErrorMessage });
});

playerApi.MapPut("/squads/{stageId}", async (
    string stageId,
    SaveStageSquadRequest request,
    PlayerActorRegistry players,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.UserId))
        return Results.BadRequest(new { errorCode = "invalid_user_id", message = "userId is required." });
    StageSquadHandleResult result = await players.Get(request.UserId)
        .SaveStageSquadAsync(stageId, request, cancellationToken);
    return result.Status switch
    {
        StageSquadHandleStatus.Accepted => Results.Ok(result.Payload),
        StageSquadHandleStatus.Conflict => Results.Conflict(new
        {
            errorCode = result.ErrorCode,
            message = result.ErrorMessage,
            current = result.Payload
        }),
        _ => Results.BadRequest(new { errorCode = result.ErrorCode, message = result.ErrorMessage })
    };
});

playerApi.MapGet("/skill-profiles/{userId}", async (
    string userId,
    PlayerActorRegistry players,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(userId))
        return Results.BadRequest(new { errorCode = "invalid_user_id", message = "userId is required." });
    SkillProfileHandleResult result = await players.Get(userId).GetSkillProfileAsync(cancellationToken);
    return result.Status == SkillProfileHandleStatus.Accepted
        ? Results.Ok(result.Payload)
        : Results.BadRequest(new { errorCode = result.ErrorCode, message = result.ErrorMessage });
});

playerApi.MapPut("/skill-profiles", async (
    SaveSkillProfileRequest request,
    PlayerActorRegistry players,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.UserId))
        return Results.BadRequest(new { errorCode = "invalid_user_id", message = "userId is required." });
    SkillProfileHandleResult result = await players.Get(request.UserId)
        .SaveSkillProfileAsync(request, cancellationToken);
    return result.Status switch
    {
        SkillProfileHandleStatus.Accepted => Results.Ok(result.Payload),
        SkillProfileHandleStatus.Conflict => Results.Conflict(new
        {
            errorCode = result.ErrorCode,
            message = result.ErrorMessage,
            current = result.Payload
        }),
        _ => Results.BadRequest(new { errorCode = result.ErrorCode, message = result.ErrorMessage })
    };
});

app.Run();

static IResult ReadinessResult(ActorThreadPool pool) => pool.IsReady
    ? Results.Ok(new { status = "ok", check = "ready" })
    : Results.Json(
        new { status = "unavailable", check = "ready" },
        statusCode: StatusCodes.Status503ServiceUnavailable);

public partial class Program;
