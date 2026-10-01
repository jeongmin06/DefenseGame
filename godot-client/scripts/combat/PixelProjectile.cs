using Godot;

namespace DefenseGame.Client.Combat;

public partial class PixelProjectile : Node2D
{
    private Enemy? _target;
    private float _damage = 1.0f;
    private const float Speed = 620.0f;

    public void Setup(Enemy target, float damage)
    {
        _target = target;
        _damage = damage;
        QueueRedraw();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_target is null || !GodotObject.IsInstanceValid(_target) || !_target.IsActive())
        {
            QueueFree();
            return;
        }

        GlobalPosition = GlobalPosition.MoveToward(_target.GlobalPosition, Speed * (float)delta);
        if (GlobalPosition.DistanceSquaredTo(_target.GlobalPosition) > 100.0f)
        {
            return;
        }

        _target.TakeDamage(_damage);
        QueueFree();
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(-5.0f, -2.0f, 10.0f, 4.0f), Rgb(255, 209, 102));
        DrawRect(new Rect2(-2.0f, -4.0f, 4.0f, 8.0f), Rgb(255, 241, 168));
    }

    private static Color Rgb(byte red, byte green, byte blue, byte alpha = 255)
    {
        return new Color(red / 255.0f, green / 255.0f, blue / 255.0f, alpha / 255.0f);
    }
}
