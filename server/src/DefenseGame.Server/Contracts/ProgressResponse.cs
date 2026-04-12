namespace DefenseGame.Server.Contracts;

public sealed record ProgressResponse(
    string UserId,
    int HighestClearedStageId,
    int NextUnlockedStageId,
    IReadOnlyList<int> ClearedStageIds,
    DateTimeOffset UpdatedAtUtc);
