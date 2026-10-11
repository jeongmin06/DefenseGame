namespace DefenseGame.Server.Domain;

public static class PlayerDefaults
{
    public static readonly string[] InitialCharacterIds =
    [
        "starter_archer_a",
        "starter_archer_b",
        "starter_warrior_a",
        "starter_healer_a"
    ];

    public static readonly string[] InitialSkillIds =
    [
        "basic_arrow",
        "basic_slash",
        "basic_heal",
        "multiple_projectiles",
        "piercing_shot",
        "fire_infusion",
        "healing_amplification"
    ];

    public static SkillProfileState CreateInitialSkillProfile(DateTimeOffset nowUtc)
    {
        SkillPresetState Preset(string characterId, string activeSkillId, params string[] supports) => new()
        {
            CharacterId = characterId,
            ActiveSkillId = activeSkillId,
            SupportSkillIds = supports.ToList(),
            AllocatedPoints = supports.Length,
            UpdatedAtUtc = nowUtc
        };

        return new SkillProfileState
        {
            PlayerLevel = 5,
            UnlockedPoints = 5,
            OwnedSkillIds = new HashSet<string>(InitialSkillIds, StringComparer.Ordinal),
            Presets = new Dictionary<string, SkillPresetState>(StringComparer.Ordinal)
            {
                ["starter_archer_a"] = Preset("starter_archer_a", "basic_arrow", "multiple_projectiles", "piercing_shot"),
                ["starter_archer_b"] = Preset("starter_archer_b", "basic_arrow", "fire_infusion"),
                ["starter_warrior_a"] = Preset("starter_warrior_a", "basic_slash"),
                ["starter_healer_a"] = Preset("starter_healer_a", "basic_heal")
            },
            Revision = 0,
            UpdatedAtUtc = nowUtc
        };
    }
}
