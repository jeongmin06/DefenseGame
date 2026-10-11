namespace DefenseGame.Server.Domain;

public sealed class SkillPresetState
{
    public required string CharacterId { get; init; }
    public required string ActiveSkillId { get; set; }
    public List<string> SupportSkillIds { get; set; } = new();
    public int AllocatedPoints { get; set; }
    public required DateTimeOffset UpdatedAtUtc { get; set; }

    public SkillPresetState Clone() => new()
    {
        CharacterId = CharacterId,
        ActiveSkillId = ActiveSkillId,
        SupportSkillIds = new List<string>(SupportSkillIds),
        AllocatedPoints = AllocatedPoints,
        UpdatedAtUtc = UpdatedAtUtc
    };
}
