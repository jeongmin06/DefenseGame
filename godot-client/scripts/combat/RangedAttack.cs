using Godot;
using DefenseGame.Client.Data;

namespace DefenseGame.Client.Combat;

public partial class RangedAttack : Node2D
{
    public PackedScene? ProjectileScene { get; set; }

    public float ProjectileSpeed { get; private set; }

    public Vector2 SpawnOffset { get; private set; }
    public int ProjectileCount { get; private set; } = 1;
    public int PierceCount { get; private set; }
    public float AddedHitDamage { get; private set; }
    public float DamageMultiplier { get; private set; } = 1.0f;
    public Godot.Collections.Array<string> AttackTags { get; private set; } = new();

    private float _hitDistance;

    public void Configure(UnitDefinition definition)
    {
        ConfigureWithSettings(definition, new RangedAttackSettings());
    }

    public void ConfigureWithSettings(UnitDefinition definition, RangedAttackSettings settings)
    {
        ProjectileScene = definition.ProjectileScene;
        ProjectileSpeed = definition.ProjectileSpeed;
        SpawnOffset = definition.ProjectileSpawnOffset;
        _hitDistance = definition.ProjectileHitDistance;
        ProjectileCount = settings.ProjectileCount;
        PierceCount = settings.PierceCount;
        AddedHitDamage = settings.AddedHitDamage;
        DamageMultiplier = settings.DamageMultiplier;
        AttackTags = new Godot.Collections.Array<string>(settings.Tags);
    }

    public void Fire(Enemy target, float damage, bool facingLeft)
    {
        if (!GodotObject.IsInstanceValid(target) || !target.IsActive())
        {
            return;
        }

        if (ProjectileScene is null)
        {
            GD.PushError("RangedAttack requires a ProjectileScene with a PixelProjectile root.");
            return;
        }

        Node currentScene = GetTree().CurrentScene;
        Node projectileLayer = currentScene.GetNodeOrNull<Node>("Projectiles") ?? currentScene;
        float horizontalOffset = facingLeft ? -SpawnOffset.X : SpawnOffset.X;
        Vector2 basePosition = GlobalPosition + new Vector2(horizontalOffset, SpawnOffset.Y);
        Vector2 direction = target.GlobalPosition - basePosition;
        Vector2 spreadAxis = direction.IsZeroApprox() ? Vector2.Down : direction.Normalized().Orthogonal();
        float damagePerHit = Mathf.Max(0.0f, damage + AddedHitDamage) * DamageMultiplier;

        for (int i = 0; i < ProjectileCount; i++)
        {
            Node instance = ProjectileScene.Instantiate();
            if (instance is not PixelProjectile projectile)
            {
                instance.Free();
                GD.PushError("RangedAttack ProjectileScene root must inherit PixelProjectile.");
                return;
            }

            projectileLayer.AddChild(projectile);
            float spread = (i - (ProjectileCount - 1) * 0.5f) * 7.0f;
            projectile.GlobalPosition = basePosition + spreadAxis * spread;
            projectile.SetupWithEffects(target, damagePerHit, ProjectileSpeed, _hitDistance, PierceCount, AttackTags);
        }
    }
}
