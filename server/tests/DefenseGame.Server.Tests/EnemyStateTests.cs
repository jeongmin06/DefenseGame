using System.Numerics;
using DefenseGame.Server.Domain;

namespace DefenseGame.Server.Tests;

public class EnemyStateTests
{
    [Fact]
    public void Spawn_InitializesEnemyAtSpawnPosition()
    {
        var enemy = EnemyState.Spawn(
            "enemy-1",
            new EnemySpawnSpec(new Vector2(1f, 2f), new Vector2(5f, 2f), 3f));

        Assert.Equal("enemy-1", enemy.EnemyId);
        Assert.Equal(new Vector2(1f, 2f), enemy.Position);
        Assert.Equal(new Vector2(5f, 2f), enemy.TargetPosition);
        Assert.Equal(3f, enemy.MoveSpeed);
        Assert.False(enemy.HasReachedGoal);
    }

    [Fact]
    public void Advance_MovesEnemyTowardTarget()
    {
        var enemy = EnemyState.Spawn(
            "enemy-1",
            new EnemySpawnSpec(Vector2.Zero, new Vector2(10f, 0f), 4f));

        enemy.Advance(0.5f);

        Assert.Equal(new Vector2(2f, 0f), enemy.Position);
        Assert.False(enemy.HasReachedGoal);
    }

    [Fact]
    public void Advance_SnapsToTarget_WhenEnemyReachesGoal()
    {
        var enemy = EnemyState.Spawn(
            "enemy-1",
            new EnemySpawnSpec(Vector2.Zero, new Vector2(1f, 0f), 5f));

        enemy.Advance(1f);

        Assert.Equal(new Vector2(1f, 0f), enemy.Position);
        Assert.True(enemy.HasReachedGoal);
    }

    [Fact]
    public void Spawn_InvalidMoveSpeed_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            EnemyState.Spawn(
                "enemy-1",
                new EnemySpawnSpec(Vector2.Zero, Vector2.One, 0f)));
    }
}
