namespace DefenseGame.Server.Domain;

public sealed class SkillProfileState
{
    public int PlayerLevel { get; init; }
    public int UnlockedPoints { get; init; }
    public HashSet<string> OwnedSkillIds { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, SkillPresetState> Presets { get; init; } = new(StringComparer.Ordinal);
    public int Revision { get; set; }
    public required DateTimeOffset UpdatedAtUtc { get; set; }

    public SkillProfileState Clone() => new()
    {
        PlayerLevel = PlayerLevel,
        UnlockedPoints = UnlockedPoints,
        OwnedSkillIds = new HashSet<string>(OwnedSkillIds, StringComparer.Ordinal),
        Presets = Presets.ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal),
        Revision = Revision,
        UpdatedAtUtc = UpdatedAtUtc
    };
}
