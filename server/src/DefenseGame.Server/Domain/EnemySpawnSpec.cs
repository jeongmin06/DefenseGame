using System.Numerics;

namespace DefenseGame.Server.Domain;

public readonly record struct EnemySpawnSpec(
    Vector2 SpawnPosition,
    Vector2 TargetPosition,
    float MoveSpeed);
