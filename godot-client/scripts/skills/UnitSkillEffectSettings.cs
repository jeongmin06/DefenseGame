namespace DefenseGame.Client.Skills;

public sealed class UnitSkillEffectSettings
{
    public float DamageMultiplier { get; init; } = 1.0f;
    public float AddedHitDamage { get; init; }
    public float HealingMultiplier { get; init; } = 1.0f;
}
