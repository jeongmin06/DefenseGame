using DefenseGame.Server.Contracts;
using DefenseGame.Server.Services;
using DefenseGame.Server.Tests.TestDoubles;

namespace DefenseGame.Server.Tests;

public class StageProgressServiceTests
{
    [Fact]
    public async Task Victory_FirstClear_UpdatesProgress_AndGrantsReward()
    {
        var clock = new FakeClock(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var store = new InMemoryProgressStore(() => clock.UtcNow);
        var sut = new StageProgressService(store, clock);

        var result = await sut.HandleStageClearAsync(
            new StageClearRequest("user-1", StageId: 1, WasVictory: true),
            CancellationToken.None);

        Assert.True(result.IsAccepted);
        var payload = Assert.IsType<StageClearResponse>(result.Payload);
        Assert.Equal("user-1", payload.UserId);
        Assert.Equal(1, payload.StageId);
        Assert.True(payload.ProgressUpdated);
        Assert.Equal(1, payload.HighestClearedStageId);
        Assert.Equal(2, payload.NextUnlockedStageId);
        Assert.Single(payload.GrantedRewards);
        Assert.Equal("server_reward_placeholder", payload.GrantedRewards[0].RewardId);
        Assert.Equal(clock.UtcNow, payload.SavedAtUtc);
    }

    [Fact]
    public async Task Defeat_DoesNotUpdateProgress_AndGrantsNoReward()
    {
        var clock = new FakeClock(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var store = new InMemoryProgressStore(() => clock.UtcNow);
        var sut = new StageProgressService(store, clock);

        var result = await sut.HandleStageClearAsync(
            new StageClearRequest("user-1", StageId: 1, WasVictory: false),
            CancellationToken.None);

        Assert.True(result.IsAccepted);
        var payload = Assert.IsType<StageClearResponse>(result.Payload);
        Assert.False(payload.ProgressUpdated);
        Assert.Equal(0, payload.HighestClearedStageId);
        Assert.Equal(1, payload.NextUnlockedStageId);
        Assert.Empty(payload.GrantedRewards);
    }

    [Fact]
    public async Task RepeatVictory_IsIdempotent_NoSecondReward()
    {
        var clock = new FakeClock(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var store = new InMemoryProgressStore(() => clock.UtcNow);
        var sut = new StageProgressService(store, clock);

        var first = await sut.HandleStageClearAsync(
            new StageClearRequest("user-1", StageId: 1, WasVictory: true),
            CancellationToken.None);

        clock.UtcNow = clock.UtcNow.AddMinutes(1);

        var second = await sut.HandleStageClearAsync(
            new StageClearRequest("user-1", StageId: 1, WasVictory: true),
            CancellationToken.None);

        Assert.True(first.IsAccepted);
        Assert.True(second.IsAccepted);

        var secondPayload = Assert.IsType<StageClearResponse>(second.Payload);
        Assert.False(secondPayload.ProgressUpdated);
        Assert.Equal(1, secondPayload.HighestClearedStageId);
        Assert.Equal(2, secondPayload.NextUnlockedStageId);
        Assert.Empty(secondPayload.GrantedRewards);
    }

    [Fact]
    public async Task SkipStage_IsRejected()
    {
        var clock = new FakeClock(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var store = new InMemoryProgressStore(() => clock.UtcNow);
        var sut = new StageProgressService(store, clock);

        var result = await sut.HandleStageClearAsync(
            new StageClearRequest("user-1", StageId: 3, WasVictory: true),
            CancellationToken.None);

        Assert.False(result.IsAccepted);
        Assert.Equal("stage_locked", result.ErrorCode);
        Assert.Null(result.Payload);
    }
}
