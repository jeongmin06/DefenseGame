using DefenseGame.Server.Actors.Runtime;
using DefenseGame.Server.Contracts;
using DefenseGame.Server.Services;

namespace DefenseGame.Server.Actors.Players;

public sealed class PlayerActor(string userId, IStageProgressService service, ActorChannel channel)
    : Actor<PlayerActor>(channel)
{
    public Task<ProgressResponse> GetProgressAsync(CancellationToken cancellationToken) =>
        AskAsync((_, token) => service.GetProgressAsync(userId, token), cancellationToken);

    public Task<StageClearHandleResult> HandleStageClearAsync(StageClearRequest request, CancellationToken cancellationToken) =>
        AskAsync((_, token) => service.HandleStageClearAsync(request with { UserId = userId }, token), cancellationToken);
}
