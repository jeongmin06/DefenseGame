using DefenseGame.Server.Contracts;

namespace DefenseGame.Server.Services;

public interface IStageSquadService
{
    Task<StageSquadHandleResult> GetAsync(string userId, string stageId, CancellationToken cancellationToken);
    Task<StageSquadHandleResult> SaveAsync(
        string userId,
        string stageId,
        SaveStageSquadRequest request,
        CancellationToken cancellationToken);
}
