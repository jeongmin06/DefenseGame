namespace DefenseGame.Server.Domain;

public enum SkillRuleRole
{
    Active,
    Support
}

public sealed record SkillRule(
    string Id,
    SkillRuleRole Role,
    int LinkCost,
    IReadOnlySet<string> Tags,
    IReadOnlySet<string> RequiredAnyTags,
    IReadOnlySet<string> RequiredAllTags,
    IReadOnlySet<string> ForbiddenTags);

public static class SkillCatalogRules
{
    public static readonly IReadOnlyDictionary<string, SkillRule> Skills = new Dictionary<string, SkillRule>(StringComparer.Ordinal)
    {
        ["basic_arrow"] = Active("basic_arrow", "ATTACK", "BOW", "PROJECTILE", "PHYSICAL", "HIT"),
        ["basic_slash"] = Active("basic_slash", "ATTACK", "PHYSICAL", "HIT"),
        ["basic_heal"] = Active("basic_heal", "HEAL"),
        ["multiple_projectiles"] = Support("multiple_projectiles", requiredAny: ["PROJECTILE"]),
        ["piercing_shot"] = Support("piercing_shot", requiredAny: ["PROJECTILE"]),
        ["fire_infusion"] = Support("fire_infusion", requiredAny: ["HIT"]),
        ["healing_amplification"] = Support("healing_amplification", requiredAny: ["HEAL"])
    };

    public static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> CharacterTags =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            ["starter_archer_a"] = Tags("ATTACK", "BOW", "PROJECTILE", "PHYSICAL", "HIT"),
            ["starter_archer_b"] = Tags("ATTACK", "BOW", "PROJECTILE", "PHYSICAL", "HIT"),
            ["starter_warrior_a"] = Tags("ATTACK", "PHYSICAL", "HIT"),
            ["starter_healer_a"] = Tags("HEAL")
        };

    private static SkillRule Active(string id, params string[] tags) =>
        new(id, SkillRuleRole.Active, 0, Tags(tags), Tags(), Tags(), Tags());

    private static SkillRule Support(
        string id,
        string[]? requiredAny = null,
        string[]? requiredAll = null,
        string[]? forbidden = null) =>
        new(id, SkillRuleRole.Support, 1, Tags(), Tags(requiredAny ?? []), Tags(requiredAll ?? []), Tags(forbidden ?? []));

    private static IReadOnlySet<string> Tags(params string[] values) =>
        new HashSet<string>(values, StringComparer.Ordinal);
}
