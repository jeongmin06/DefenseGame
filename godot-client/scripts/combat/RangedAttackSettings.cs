using Godot;

namespace DefenseGame.Client.Combat;

// Runtime-only values copied from a validated skill loadout.
[GlobalClass]
public partial class RangedAttackSettings : RefCounted
{
    public int ProjectileCount { get; set; } = 1;
    public int PierceCount { get; set; }
    public float AddedHitDamage { get; set; }
    public float DamageMultiplier { get; set; } = 1.0f;
    public Godot.Collections.Array<string> Tags { get; set; } = new();

    public float GetDamagePerHit(float baseDamage) =>
        Mathf.Max(0.0f, baseDamage + AddedHitDamage) * DamageMultiplier;
}
