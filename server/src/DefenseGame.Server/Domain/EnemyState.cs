using System.Numerics;

namespace DefenseGame.Server.Domain;

public sealed class EnemyState
{
    public required string EnemyId { get; init; }
    public Vector2 Position { get; private set; }
    public required Vector2 TargetPosition { get; init; }
    public required float MoveSpeed { get; init; }

    public bool HasReachedGoal { get; private set; }

    public static EnemyState Spawn(string enemyId, EnemySpawnSpec spawnSpec)
    {
        if (string.IsNullOrWhiteSpace(enemyId))
        {
            throw new ArgumentException("enemyId is required.", nameof(enemyId));
        }

        if (!IsFinite(spawnSpec.SpawnPosition))
        {
            throw new ArgumentException("spawn position must be finite.", nameof(spawnSpec));
        }

        if (!IsFinite(spawnSpec.TargetPosition))
        {
            throw new ArgumentException("target position must be finite.", nameof(spawnSpec));
        }

        if (!float.IsFinite(spawnSpec.MoveSpeed) || spawnSpec.MoveSpeed <= 0f)
        {
            throw new ArgumentException("move speed must be a positive finite value.", nameof(spawnSpec));
        }

        return new EnemyState
        {
            EnemyId = enemyId.Trim(),
            Position = spawnSpec.SpawnPosition,
            TargetPosition = spawnSpec.TargetPosition,
            MoveSpeed = spawnSpec.MoveSpeed,
            HasReachedGoal = spawnSpec.SpawnPosition == spawnSpec.TargetPosition
        };
    }

    public void Advance(float deltaTimeSeconds)
    {
        if (!float.IsFinite(deltaTimeSeconds) || deltaTimeSeconds < 0f)
        {
            throw new ArgumentException("deltaTimeSeconds must be a finite non-negative value.", nameof(deltaTimeSeconds));
        }

        if (HasReachedGoal || deltaTimeSeconds == 0f)
        {
            return;
        }

        var toTarget = TargetPosition - Position;
        var distanceToTarget = toTarget.Length();

        if (distanceToTarget <= 0f)
        {
            Position = TargetPosition;
            HasReachedGoal = true;
            return;
        }

        var moveDistance = MoveSpeed * deltaTimeSeconds;
        if (moveDistance >= distanceToTarget)
        {
            Position = TargetPosition;
            HasReachedGoal = true;
            return;
        }

        Position += Vector2.Normalize(toTarget) * moveDistance;
    }

    private static bool IsFinite(Vector2 value)
    {
        return float.IsFinite(value.X) && float.IsFinite(value.Y);
    }
}
