using DefenseGame.Server.Actors.Players;
using DefenseGame.Server.Actors.Runtime;
using DefenseGame.Server.Contracts;
using DefenseGame.Server.Services;
using DefenseGame.Server.Stores;

var builder = WebApplication.CreateBuilder(args);

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

builder.Services.AddSingleton<ActorThreadScheduler>();
builder.Services.AddHostedService<ActorThreadPool>();
builder.Services.AddSingleton<PlayerActorRegistry>();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapGet("/v1/progress/{userId}", async (
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

app.MapPost("/v1/stage-clear", async (
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

app.Run();

public partial class Program;
