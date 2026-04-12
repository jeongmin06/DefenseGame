namespace DefenseGame.Server.Contracts;

public sealed record StageClearResponse(
    string UserId,
    int StageId,
    bool ProgressUpdated,
    int HighestClearedStageId,
    int NextUnlockedStageId,
    IReadOnlyList<RewardDto> GrantedRewards,
    DateTimeOffset SavedAtUtc);
