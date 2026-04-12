using DefenseGame.Server.Contracts;

namespace DefenseGame.Server.Services;

public interface IStageProgressService
{
    Task<ProgressResponse> GetProgressAsync(string userId, CancellationToken cancellationToken);
    Task<StageClearHandleResult> HandleStageClearAsync(StageClearRequest request, CancellationToken cancellationToken);
}
