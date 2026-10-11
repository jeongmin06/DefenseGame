namespace DefenseGame.Server.Contracts;

public sealed record SaveSkillPresetRequest(
    string CharacterId,
    string ActiveSkillId,
    IReadOnlyList<string>? SupportSkillIds,
    int AllocatedPoints);

public sealed record SaveSkillProfileRequest(
    string UserId,
    IReadOnlyList<SaveSkillPresetRequest>? Presets,
    int ExpectedRevision,
    bool IsMigration);
