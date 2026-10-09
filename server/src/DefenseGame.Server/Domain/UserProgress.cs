namespace DefenseGame.Server.Domain;

public sealed class UserProgress
{
    public required string UserId { get; init; }
    public required HashSet<int> ClearedStageIds { get; init; }
    public HashSet<string> OwnedCharacterIds { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, StageSquadState> StageSquads { get; init; } = new(StringComparer.Ordinal);
    public required DateTimeOffset UpdatedAtUtc { get; set; }

    public int HighestClearedStageId => ClearedStageIds.Count == 0 ? 0 : ClearedStageIds.Max();
    public int NextUnlockedStageId => HighestClearedStageId + 1;

    public static UserProgress New(string userId, DateTimeOffset nowUtc)
    {
        return new UserProgress
        {
            UserId = userId,
            ClearedStageIds = new HashSet<int>(),
            OwnedCharacterIds = new HashSet<string>(PlayerDefaults.InitialCharacterIds, StringComparer.Ordinal),
            StageSquads = new Dictionary<string, StageSquadState>(StringComparer.Ordinal),
            UpdatedAtUtc = nowUtc
        };
    }

    public UserProgress Clone()
    {
        return new UserProgress
        {
            UserId = UserId,
            ClearedStageIds = new HashSet<int>(ClearedStageIds),
            OwnedCharacterIds = new HashSet<string>(OwnedCharacterIds, StringComparer.Ordinal),
            StageSquads = StageSquads.ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal),
            UpdatedAtUtc = UpdatedAtUtc
        };
    }
}
