using Godot;

namespace DefenseGame.Client.Combat;

public partial class RangedAttack : Node2D
{
    [Export]
    public PackedScene? ProjectileScene { get; set; }

    [Export]
    public float ProjectileSpeed { get; set; } = 620.0f;

    [Export]
    public Vector2 SpawnOffset { get; set; } = new(28.0f, -28.0f);

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

        Node instance = ProjectileScene.Instantiate();
        if (instance is not PixelProjectile projectile)
        {
            instance.Free();
            GD.PushError("RangedAttack ProjectileScene root must inherit PixelProjectile.");
            return;
        }

        Node currentScene = GetTree().CurrentScene;
        Node projectileLayer = currentScene.GetNodeOrNull<Node>("Projectiles") ?? currentScene;
        projectileLayer.AddChild(projectile);
        float horizontalOffset = facingLeft ? -SpawnOffset.X : SpawnOffset.X;
        projectile.GlobalPosition = GlobalPosition + new Vector2(horizontalOffset, SpawnOffset.Y);
        projectile.Setup(target, damage, ProjectileSpeed);
    }
}
