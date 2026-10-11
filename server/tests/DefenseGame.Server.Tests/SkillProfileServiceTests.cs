using DefenseGame.Server.Contracts;
using DefenseGame.Server.Services;
using DefenseGame.Server.Tests.TestDoubles;

namespace DefenseGame.Server.Tests;

public sealed class SkillProfileServiceTests
{
    private readonly FakeClock _clock = new(DateTimeOffset.Parse("2026-10-11T10:00:00Z"));
    private readonly SkillProfileService _service;

    public SkillProfileServiceTests()
    {
        _service = new SkillProfileService(new InMemoryProgressStore(() => _clock.UtcNow), _clock);
    }

    [Fact]
    public async Task NewProfile_ReturnsServerOwnedDefaultsAtRevisionZero()
    {
        SkillProfileHandleResult result = await _service.GetAsync(" player ", default);

        Assert.Equal(SkillProfileHandleStatus.Accepted, result.Status);
        Assert.Equal("player", result.Payload!.UserId);
        Assert.Equal(5, result.Payload.PlayerLevel);
        Assert.Equal(5, result.Payload.UnlockedPoints);
        Assert.Equal(7, result.Payload.OwnedSkillIds.Count);
        Assert.Equal(4, result.Payload.Presets.Count);
        Assert.Equal(0, result.Payload.Revision);
    }

    [Fact]
    public async Task Save_PreservesSupportOrder_AndIncrementsRevision()
    {
        SaveSkillProfileRequest request = Request(0,
            ArcherA("fire_infusion", "multiple_projectiles"));

        SkillProfileHandleResult saved = await _service.SaveAsync("player", request, default);

        Assert.Equal(SkillProfileHandleStatus.Accepted, saved.Status);
        Assert.Equal(1, saved.Payload!.Revision);
        Assert.Equal(["fire_infusion", "multiple_projectiles"],
            saved.Payload.Presets.Single(preset => preset.CharacterId == "starter_archer_a").SupportSkillIds);
        Assert.All(saved.Payload.Presets, preset => Assert.Equal(_clock.UtcNow, preset.UpdatedAtUtc));
    }

    [Fact]
    public async Task StaleRevision_ReturnsCurrentServerProfile()
    {
        await _service.SaveAsync("player", Request(0), default);

        SkillProfileHandleResult conflict = await _service.SaveAsync("player", Request(0), default);

        Assert.Equal(SkillProfileHandleStatus.Conflict, conflict.Status);
        Assert.Equal(1, conflict.Payload!.Revision);
    }

    [Theory]
    [InlineData("healing_amplification", "incompatible_support")]
    [InlineData("basic_slash", "skill_role_mismatch")]
    [InlineData("not_owned", "unknown_skill")]
    public async Task InvalidArcherSupport_IsRejected(string supportId, string errorCode)
    {
        SkillProfileHandleResult result = await _service.SaveAsync("player",
            Request(0, ArcherA(supportId)), default);

        Assert.Equal(SkillProfileHandleStatus.Rejected, result.Status);
        Assert.Equal(errorCode, result.ErrorCode);
    }

    [Fact]
    public async Task DuplicateSupport_IsRejected()
    {
        SkillProfileHandleResult result = await _service.SaveAsync("player",
            Request(0, ArcherA("fire_infusion", "fire_infusion")), default);

        Assert.Equal("duplicate_support", result.ErrorCode);
    }

    [Fact]
    public async Task TotalUnlockedPoints_AreEnforced()
    {
        SaveSkillPresetRequest[] presets = ValidPresets();
        presets[0] = ArcherA("multiple_projectiles", "piercing_shot", "fire_infusion");
        presets[1] = new SaveSkillPresetRequest("starter_archer_b", "basic_arrow",
            ["multiple_projectiles", "piercing_shot", "fire_infusion"], 3);

        SkillProfileHandleResult result = await _service.SaveAsync("player",
            new SaveSkillProfileRequest("player", presets, 0, false), default);

        Assert.Equal("skill_budget_exceeded", result.ErrorCode);
    }

    [Fact]
    public async Task Migration_IsAcceptedOnlyAtRevisionZero()
    {
        SkillProfileHandleResult migrated = await _service.SaveAsync("player", Request(0, isMigration: true), default);
        SkillProfileHandleResult repeated = await _service.SaveAsync("player", Request(1, isMigration: true), default);

        Assert.Equal(SkillProfileHandleStatus.Accepted, migrated.Status);
        Assert.Equal("migration_already_completed", repeated.ErrorCode);
    }

    private static SaveSkillProfileRequest Request(
        int revision,
        SaveSkillPresetRequest? replacement = null,
        bool isMigration = false)
    {
        SaveSkillPresetRequest[] presets = ValidPresets();
        if (replacement is not null) presets[0] = replacement;
        return new SaveSkillProfileRequest("ignored", presets, revision, isMigration);
    }

    private static SaveSkillPresetRequest ArcherA(params string[] supports) =>
        new("starter_archer_a", "basic_arrow", supports, supports.Length);

    private static SaveSkillPresetRequest[] ValidPresets() =>
    [
        ArcherA("multiple_projectiles", "piercing_shot"),
        new("starter_archer_b", "basic_arrow", ["fire_infusion"], 1),
        new("starter_warrior_a", "basic_slash", [], 0),
        new("starter_healer_a", "basic_heal", [], 0)
    ];
}
