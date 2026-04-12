namespace DefenseGame.Server.Domain;

public sealed class UserProgress
{
    public required string UserId { get; init; }
    public required HashSet<int> ClearedStageIds { get; init; }
    public required DateTimeOffset UpdatedAtUtc { get; set; }

    public int HighestClearedStageId => ClearedStageIds.Count == 0 ? 0 : ClearedStageIds.Max();
    public int NextUnlockedStageId => HighestClearedStageId + 1;

    public static UserProgress New(string userId, DateTimeOffset nowUtc)
    {
        return new UserProgress
        {
            UserId = userId,
            ClearedStageIds = new HashSet<int>(),
            UpdatedAtUtc = nowUtc
        };
    }

    public UserProgress Clone()
    {
        return new UserProgress
        {
            UserId = UserId,
            ClearedStageIds = new HashSet<int>(ClearedStageIds),
            UpdatedAtUtc = UpdatedAtUtc
        };
    }
}
