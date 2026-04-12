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

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapGet("/v1/progress/{userId}", async (
    string userId,
    IStageProgressService stageProgressService,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(userId))
    {
        return Results.BadRequest(new { errorCode = "invalid_user_id", message = "userId is required." });
    }

    var progress = await stageProgressService.GetProgressAsync(userId, cancellationToken);
    return Results.Ok(progress);
});

app.MapPost("/v1/stage-clear", async (
    StageClearRequest request,
    IStageProgressService stageProgressService,
    CancellationToken cancellationToken) =>
{
    var result = await stageProgressService.HandleStageClearAsync(request, cancellationToken);
    return result.IsAccepted
        ? Results.Ok(result.Payload)
        : Results.BadRequest(new { errorCode = result.ErrorCode, message = result.ErrorMessage });
});

app.Run();

public partial class Program;
