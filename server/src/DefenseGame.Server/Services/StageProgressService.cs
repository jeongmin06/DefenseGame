using DefenseGame.Server.Contracts;
using DefenseGame.Server.Domain;
using DefenseGame.Server.Stores;

namespace DefenseGame.Server.Services;

public sealed class StageProgressService : IStageProgressService
{
    private readonly IProgressStore _progressStore;
    private readonly ISystemClock _clock;

    public StageProgressService(IProgressStore progressStore, ISystemClock clock)
    {
        _progressStore = progressStore;
        _clock = clock;
    }

    public async Task<ProgressResponse> GetProgressAsync(string userId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("userId is required", nameof(userId));
        }

        var progress = await _progressStore.GetOrCreateAsync(userId.Trim(), cancellationToken);
        return ToProgressResponse(progress);
    }

    public async Task<StageClearHandleResult> HandleStageClearAsync(StageClearRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.UserId))
        {
            return StageClearHandleResult.Rejected("invalid_user_id", "userId is required.");
        }

        if (request.StageId <= 0)
        {
            return StageClearHandleResult.Rejected("invalid_stage_id", "stageId must be a positive integer.");
        }

        var userId = request.UserId.Trim();
        var progress = await _progressStore.GetOrCreateAsync(userId, cancellationToken);

        if (request.StageId > progress.NextUnlockedStageId)
        {
            return StageClearHandleResult.Rejected(
                "stage_locked",
                $"Cannot clear stage {request.StageId} before stage {progress.NextUnlockedStageId}.");
        }

        var now = _clock.UtcNow;

        if (!request.WasVictory)
        {
            var defeatResponse = new StageClearResponse(
                UserId: userId,
                StageId: request.StageId,
                ProgressUpdated: false,
                HighestClearedStageId: progress.HighestClearedStageId,
                NextUnlockedStageId: progress.NextUnlockedStageId,
                GrantedRewards: Array.Empty<RewardDto>(),
                SavedAtUtc: progress.UpdatedAtUtc);

            return StageClearHandleResult.Accepted(defeatResponse);
        }

        if (progress.ClearedStageIds.Contains(request.StageId))
        {
            var repeatClearResponse = new StageClearResponse(
                UserId: userId,
                StageId: request.StageId,
                ProgressUpdated: false,
                HighestClearedStageId: progress.HighestClearedStageId,
                NextUnlockedStageId: progress.NextUnlockedStageId,
                GrantedRewards: Array.Empty<RewardDto>(),
                SavedAtUtc: progress.UpdatedAtUtc);

            return StageClearHandleResult.Accepted(repeatClearResponse);
        }

        progress.ClearedStageIds.Add(request.StageId);
        progress.UpdatedAtUtc = now;
        var saved = await _progressStore.UpsertAsync(progress, cancellationToken);

        var rewards = new[]
        {
            new RewardDto(
                RewardId: "server_reward_placeholder",
                Amount: 100,
                Reason: "first_clear")
        };

        var successResponse = new StageClearResponse(
            UserId: userId,
            StageId: request.StageId,
            ProgressUpdated: true,
            HighestClearedStageId: saved.HighestClearedStageId,
            NextUnlockedStageId: saved.NextUnlockedStageId,
            GrantedRewards: rewards,
            SavedAtUtc: saved.UpdatedAtUtc);

        return StageClearHandleResult.Accepted(successResponse);
    }

    private static ProgressResponse ToProgressResponse(UserProgress progress)
    {
        var orderedClearedStageIds = progress.ClearedStageIds.OrderBy(x => x).ToArray();
        return new ProgressResponse(
            UserId: progress.UserId,
            HighestClearedStageId: progress.HighestClearedStageId,
            NextUnlockedStageId: progress.NextUnlockedStageId,
            ClearedStageIds: orderedClearedStageIds,
            UpdatedAtUtc: progress.UpdatedAtUtc);
    }
}
