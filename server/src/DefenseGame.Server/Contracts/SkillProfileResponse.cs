namespace DefenseGame.Server.Contracts;

public sealed record SkillPresetResponse(
    string CharacterId,
    string ActiveSkillId,
    IReadOnlyList<string> SupportSkillIds,
    int AllocatedPoints,
    DateTimeOffset UpdatedAtUtc);

public sealed record SkillProfileResponse(
    string UserId,
    int PlayerLevel,
    int UnlockedPoints,
    IReadOnlyList<string> OwnedSkillIds,
    IReadOnlyList<SkillPresetResponse> Presets,
    int Revision,
    DateTimeOffset UpdatedAtUtc);
