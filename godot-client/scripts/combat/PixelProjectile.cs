using Godot;

namespace DefenseGame.Client.Combat;

public partial class PixelProjectile : Node2D
{
    private Enemy? _target;
    private float _speed;
    private float _hitDistanceSquared;
    private readonly System.Collections.Generic.HashSet<ulong> _hitEnemyIds = new();

    public float Damage { get; private set; }
    public int RemainingPierces { get; private set; }
    public Godot.Collections.Array<string> HitTags { get; private set; } = new();

    public void Setup(Enemy target, float damage, float speed, float hitDistance)
    {
        SetupWithEffects(target, damage, speed, hitDistance, 0, new Godot.Collections.Array<string>());
    }

    public void SetupWithEffects(
        Enemy target,
        float damage,
        float speed,
        float hitDistance,
        int pierceCount,
        Godot.Collections.Array<string> hitTags)
    {
        _target = target;
        Damage = damage;
        _speed = speed;
        _hitDistanceSquared = hitDistance * hitDistance;
        RemainingPierces = Mathf.Max(0, pierceCount);
        HitTags = new Godot.Collections.Array<string>(hitTags);
        FaceTarget();
        QueueRedraw();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_target is null || !GodotObject.IsInstanceValid(_target) || !_target.IsActive())
        {
            _target = FindNextTarget();
            if (_target is null)
            {
                QueueFree();
                return;
            }
        }

        FaceTarget();
        GlobalPosition = GlobalPosition.MoveToward(_target.GlobalPosition, _speed * (float)delta);
        if (GlobalPosition.DistanceSquaredTo(_target.GlobalPosition) > _hitDistanceSquared)
        {
            return;
        }

        Enemy hit = _target;
        _hitEnemyIds.Add(hit.GetInstanceId());
        hit.TakeDamage(Damage);
        if (RemainingPierces <= 0)
        {
            QueueFree();
            return;
        }

        Enemy? next = FindNextTarget();
        if (next is null)
        {
            QueueFree();
            return;
        }

        RemainingPierces--;
        _target = next;
    }

    private Enemy? FindNextTarget()
    {
        Enemy? best = null;
        float bestDistance = float.MaxValue;
        ulong bestId = ulong.MaxValue;
        foreach (Node node in GetTree().GetNodesInGroup("enemies"))
        {
            if (node is not Enemy candidate || !GodotObject.IsInstanceValid(candidate)
                || candidate.IsQueuedForDeletion() || !candidate.IsActive()) continue;
            ulong id = candidate.GetInstanceId();
            if (_hitEnemyIds.Contains(id)) continue;
            float distance = GlobalPosition.DistanceSquaredTo(candidate.GlobalPosition);
            if (distance < bestDistance || (Mathf.IsEqualApprox(distance, bestDistance) && id < bestId))
            {
                best = candidate;
                bestDistance = distance;
                bestId = id;
            }
        }
        return best;
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
