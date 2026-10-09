using DefenseGame.Server.Contracts;
using DefenseGame.Server.Services;
using DefenseGame.Server.Tests.TestDoubles;

namespace DefenseGame.Server.Tests;

public sealed class StageSquadServiceTests
{
    private readonly FakeClock _clock = new(DateTimeOffset.Parse("2026-10-09T10:00:00Z"));
    private readonly InMemoryProgressStore _store;
    private readonly StageSquadService _service;

    public StageSquadServiceTests()
    {
        _store = new InMemoryProgressStore(() => _clock.UtcNow);
        _service = new StageSquadService(_store, _clock);
    }

    [Fact]
    public async Task MissingSquad_ReturnsRevisionZero()
    {
        StageSquadHandleResult result = await _service.GetAsync("user", "stage_01", default);
        Assert.Equal(StageSquadHandleStatus.Accepted, result.Status);
        Assert.Empty(result.Payload!.CharacterIds);
        Assert.Equal(0, result.Payload.Revision);
    }

    [Fact]
    public async Task Save_PreservesOrder_AndIncrementsRevision()
    {
        StageSquadHandleResult saved = await _service.SaveAsync("user", "stage_01",
            new SaveStageSquadRequest("ignored", ["starter_warrior_a", "starter_archer_b"], 0), default);
        Assert.Equal(StageSquadHandleStatus.Accepted, saved.Status);
        Assert.Equal(["starter_warrior_a", "starter_archer_b"], saved.Payload!.CharacterIds);
        Assert.Equal(1, saved.Payload.Revision);

        _clock.UtcNow = _clock.UtcNow.AddMinutes(1);
        StageSquadHandleResult updated = await _service.SaveAsync("user", "stage_01",
            new SaveStageSquadRequest("ignored", ["starter_healer_a"], 1), default);
        Assert.Equal(2, updated.Payload!.Revision);
        Assert.Equal(["starter_healer_a"], updated.Payload.CharacterIds);
    }

    [Fact]
    public async Task StaleRevision_ReturnsCurrentServerSquad()
    {
        await _service.SaveAsync("user", "stage_01",
            new SaveStageSquadRequest("user", ["starter_archer_a"], 0), default);
        StageSquadHandleResult conflict = await _service.SaveAsync("user", "stage_01",
            new SaveStageSquadRequest("user", ["starter_archer_b"], 0), default);
        Assert.Equal(StageSquadHandleStatus.Conflict, conflict.Status);
        Assert.Equal(["starter_archer_a"], conflict.Payload!.CharacterIds);
        Assert.Equal(1, conflict.Payload.Revision);
    }

    [Theory]
    [InlineData("starter_archer_a", "starter_archer_a", "duplicate_character")]
    [InlineData("starter_archer_a", "removed_cat", "character_not_owned")]
    public async Task InvalidCharacters_AreRejected(string first, string second, string errorCode)
    {
        StageSquadHandleResult result = await _service.SaveAsync("user", "stage_01",
            new SaveStageSquadRequest("user", [first, second], 0), default);
        Assert.Equal(StageSquadHandleStatus.Rejected, result.Status);
        Assert.Equal(errorCode, result.ErrorCode);
    }

    [Fact]
    public async Task UnknownStage_IsRejected()
    {
        StageSquadHandleResult result = await _service.SaveAsync("user", "stage_99",
            new SaveStageSquadRequest("user", ["starter_archer_a"], 0), default);
        Assert.Equal("invalid_stage_id", result.ErrorCode);
    }
}
