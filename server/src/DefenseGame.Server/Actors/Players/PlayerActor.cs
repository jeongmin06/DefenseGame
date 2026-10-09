using DefenseGame.Server.Actors.Runtime;
using DefenseGame.Server.Contracts;
using DefenseGame.Server.Services;

namespace DefenseGame.Server.Actors.Players;

public sealed class PlayerActor(
    string userId,
    IStageProgressService progressService,
    IStageSquadService squadService,
    ActorChannel channel)
    : Actor<PlayerActor>(channel)
{
    public Task<ProgressResponse> GetProgressAsync(CancellationToken cancellationToken) =>
        AskAsync((_, token) => progressService.GetProgressAsync(userId, token), cancellationToken);

    public Task<StageClearHandleResult> HandleStageClearAsync(StageClearRequest request, CancellationToken cancellationToken) =>
        AskAsync((_, token) => progressService.HandleStageClearAsync(request with { UserId = userId }, token), cancellationToken);

    public Task<StageSquadHandleResult> GetStageSquadAsync(string stageId, CancellationToken cancellationToken) =>
        AskAsync((_, token) => squadService.GetAsync(userId, stageId, token), cancellationToken);

    public Task<StageSquadHandleResult> SaveStageSquadAsync(
        string stageId,
        SaveStageSquadRequest request,
        CancellationToken cancellationToken) =>
        AskAsync((_, token) => squadService.SaveAsync(userId, stageId, request with { UserId = userId }, token), cancellationToken);
}
