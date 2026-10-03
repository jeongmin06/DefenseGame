using Godot;

namespace DefenseGame.Client.Combat;

public partial class PixelProjectile : Node2D
{
    private Enemy? _target;
    private float _damage = 1.0f;
    private float _speed = 620.0f;

    public void Setup(Enemy target, float damage, float speed = 620.0f)
    {
        _target = target;
        _damage = damage;
        _speed = speed;
        FaceTarget();
        QueueRedraw();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_target is null || !GodotObject.IsInstanceValid(_target) || !_target.IsActive())
        {
            QueueFree();
            return;
        }

        FaceTarget();
        GlobalPosition = GlobalPosition.MoveToward(_target.GlobalPosition, _speed * (float)delta);
        if (GlobalPosition.DistanceSquaredTo(_target.GlobalPosition) > 100.0f)
        {
            return;
        }

        _target.TakeDamage(_damage);
        QueueFree();
    }

    private void FaceTarget()
    {
        if (_target is null || !GodotObject.IsInstanceValid(_target))
        {
            return;
        }

        Vector2 direction = _target.GlobalPosition - GlobalPosition;
        if (!direction.IsZeroApprox())
        {
            GlobalRotation = direction.Angle();
        }
    }

    public override void _Draw()
    {
        // The arrow points along local +X; rotation follows its actual travel direction.
        DrawRect(new Rect2(-10, -1, 17, 2), Rgb(218, 163, 76));
        DrawRect(new Rect2(-7, -1, 13, 1), Rgb(255, 224, 148));
        DrawColoredPolygon(new Vector2[] { new(4, -4), new(11, 0), new(4, 4), new(6, 0) }, Rgb(255, 235, 170));
        DrawColoredPolygon(new Vector2[] { new(-11, -4), new(-6, -3), new(-3, 0), new(-8, 0) }, Rgb(255, 207, 102));
        DrawColoredPolygon(new Vector2[] { new(-11, 4), new(-6, 3), new(-3, 0), new(-8, 0) }, Rgb(231, 184, 86));
    }

    private static Color Rgb(byte red, byte green, byte blue, byte alpha = 255)
    {
        return new Color(red / 255.0f, green / 255.0f, blue / 255.0f, alpha / 255.0f);
    }
}
