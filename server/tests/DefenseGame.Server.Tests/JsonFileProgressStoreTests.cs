using DefenseGame.Server.Domain;
using DefenseGame.Server.Stores;
using DefenseGame.Server.Tests.TestDoubles;

namespace DefenseGame.Server.Tests;

public class JsonFileProgressStoreTests
{
    [Fact]
    public async Task Upsert_ThenReload_PreservesUserProgress()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "defensegame-server-tests", Guid.NewGuid().ToString("N"));
        var filePath = Path.Combine(tempDir, "progress.json");
        var now = DateTimeOffset.Parse("2026-01-01T00:00:00Z");

        var clock = new FakeClock(now);
        var firstStore = new JsonFileProgressStore(filePath, clock);
        var progress = new UserProgress
        {
            UserId = "user-1",
            ClearedStageIds = new HashSet<int> { 1, 2 },
            OwnedCharacterIds = new HashSet<string>(PlayerDefaults.InitialCharacterIds, StringComparer.Ordinal),
            StageSquads = new Dictionary<string, StageSquadState>(StringComparer.Ordinal)
            {
                ["stage_01"] = new StageSquadState
                {
                    StageId = "stage_01",
                    CharacterIds = ["starter_archer_b", "starter_warrior_a"],
                    Revision = 3,
                    UpdatedAtUtc = now
                }
            },
            SkillProfile = PlayerDefaults.CreateInitialSkillProfile(now),
            UpdatedAtUtc = now
        };

        await firstStore.UpsertAsync(progress, CancellationToken.None);

        var reloadedStore = new JsonFileProgressStore(filePath, clock);
        var loaded = await reloadedStore.GetOrCreateAsync("user-1", CancellationToken.None);

        Assert.Equal("user-1", loaded.UserId);
        Assert.Equal(2, loaded.HighestClearedStageId);
        Assert.Equal(3, loaded.NextUnlockedStageId);
        Assert.Equal(new[] { 1, 2 }, loaded.ClearedStageIds.OrderBy(x => x).ToArray());
        Assert.Equal(now, loaded.UpdatedAtUtc);
        Assert.Equal(PlayerDefaults.InitialCharacterIds.Order(StringComparer.Ordinal),
            loaded.OwnedCharacterIds.Order(StringComparer.Ordinal));
        Assert.Equal(3, loaded.StageSquads["stage_01"].Revision);
        Assert.Equal(["starter_archer_b", "starter_warrior_a"], loaded.StageSquads["stage_01"].CharacterIds);
        Assert.Equal(5, loaded.SkillProfile.UnlockedPoints);
        Assert.Equal(["multiple_projectiles", "piercing_shot"],
            loaded.SkillProfile.Presets["starter_archer_a"].SupportSkillIds);
        Assert.Equal(0, loaded.SkillProfile.Revision);
    }

    [Fact]
    public async Task LegacyFile_WithoutSkillProfile_LoadsServerDefaults()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "defensegame-server-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "progress.json");
        await File.WriteAllTextAsync(filePath, """
        {
          "users": {
            "legacy-user": {
              "clearedStageIds": [1],
              "ownedCharacterIds": ["starter_archer_a", "starter_archer_b", "starter_warrior_a", "starter_healer_a"],
              "stageSquads": {},
              "updatedAtUtc": "2026-01-01T00:00:00+00:00"
            }
          }
        }
        """);

        var store = new JsonFileProgressStore(filePath,
            new FakeClock(DateTimeOffset.Parse("2026-02-01T00:00:00Z")));
        UserProgress loaded = await store.GetOrCreateAsync("legacy-user", default);

        Assert.Equal(5, loaded.SkillProfile.PlayerLevel);
        Assert.Equal(4, loaded.SkillProfile.Presets.Count);
        Assert.Equal(0, loaded.SkillProfile.Revision);
    }

    [Fact]
    public async Task SecondWrite_PreservesPreviousFileAsBackup_AndRemovesTemporaryFile()
    {
        var (filePath, store, now) = CreateStore();
        UserProgress progress = UserProgress.New("backup-user", now);
        await store.UpsertAsync(progress, default);

        progress.ClearedStageIds.Add(1);
        await store.UpsertAsync(progress, default);

        Assert.True(File.Exists(filePath));
        Assert.True(File.Exists($"{filePath}.bak"));
        Assert.False(File.Exists($"{filePath}.tmp"));
        var backupStore = new JsonFileProgressStore($"{filePath}.bak", new FakeClock(now));
        UserProgress backup = await backupStore.GetOrCreateAsync("backup-user", default);
        Assert.Empty(backup.ClearedStageIds);
    }

    [Fact]
    public async Task MissingPrimary_WithValidBackup_RestoresBackup()
    {
        var (filePath, store, now) = CreateStore();
        UserProgress progress = UserProgress.New("missing-primary", now);
        progress.ClearedStageIds.Add(1);
        await store.UpsertAsync(progress, default);
        File.Copy(filePath, $"{filePath}.bak");
        File.Delete(filePath);

        var recoveredStore = new JsonFileProgressStore(filePath, new FakeClock(now));
        UserProgress recovered = await recoveredStore.GetOrCreateAsync("missing-primary", default);

        Assert.Contains(1, recovered.ClearedStageIds);
        Assert.True(File.Exists(filePath));
        Assert.False(File.Exists($"{filePath}.tmp"));
    }

    [Fact]
    public async Task CorruptedPrimary_WithValidBackup_RestoresPreviousVersion()
    {
        var (filePath, store, now) = CreateStore();
        UserProgress progress = UserProgress.New("corrupt-primary", now);
        await store.UpsertAsync(progress, default);
        progress.ClearedStageIds.Add(1);
        await store.UpsertAsync(progress, default);
        await File.WriteAllTextAsync(filePath, "{corrupted");

        var recoveredStore = new JsonFileProgressStore(filePath, new FakeClock(now));
        UserProgress recovered = await recoveredStore.GetOrCreateAsync("corrupt-primary", default);

        Assert.Empty(recovered.ClearedStageIds);
        var reloadedStore = new JsonFileProgressStore(filePath, new FakeClock(now));
        UserProgress reloaded = await reloadedStore.GetOrCreateAsync("corrupt-primary", default);
        Assert.Empty(reloaded.ClearedStageIds);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"users\":null}")]
    public async Task InvalidPrimaryStructure_WithValidBackup_RestoresPreviousVersion(string invalidJson)
    {
        var (filePath, store, now) = CreateStore();
        UserProgress progress = UserProgress.New("invalid-structure", now);
        await store.UpsertAsync(progress, default);
        progress.ClearedStageIds.Add(1);
        await store.UpsertAsync(progress, default);
        await File.WriteAllTextAsync(filePath, invalidJson);

        var recoveredStore = new JsonFileProgressStore(filePath, new FakeClock(now));
        UserProgress recovered = await recoveredStore.GetOrCreateAsync("invalid-structure", default);

        Assert.Empty(recovered.ClearedStageIds);
        Assert.Contains("invalid-structure", await File.ReadAllTextAsync($"{filePath}.bak"));
    }

    [Fact]
    public async Task CorruptedPrimaryAndBackup_ThrowsWithoutReplacingData()
    {
        var (filePath, _, now) = CreateStore();
        await File.WriteAllTextAsync(filePath, "{broken-primary");
        await File.WriteAllTextAsync($"{filePath}.bak", "{broken-backup");
        var store = new JsonFileProgressStore(filePath, new FakeClock(now));

        InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(
            () => store.GetOrCreateAsync("must-not-reset", default));

        Assert.Contains("both corrupted", error.Message);
        Assert.Equal("{broken-primary", await File.ReadAllTextAsync(filePath));
        Assert.Equal("{broken-backup", await File.ReadAllTextAsync($"{filePath}.bak"));
    }

    [Fact]
    public async Task ConcurrentUserWrites_PreserveEveryUser()
    {
        var (filePath, store, now) = CreateStore();
        const int userCount = 12;

        await Task.WhenAll(Enumerable.Range(0, userCount).Select(index =>
            store.UpsertAsync(UserProgress.New($"parallel-{index}", now), default)));

        var reloadedStore = new JsonFileProgressStore(filePath, new FakeClock(now));
        for (var index = 0; index < userCount; index++)
        {
            UserProgress loaded = await reloadedStore.GetOrCreateAsync($"parallel-{index}", default);
            Assert.Equal($"parallel-{index}", loaded.UserId);
        }
    }

    [Fact]
    public async Task ReplaceFailure_PreservesPrimaryAndRemovesTemporaryFile()
    {
        var (filePath, store, now) = CreateStore();
        UserProgress progress = UserProgress.New("replace-failure", now);
        await store.UpsertAsync(progress, default);
        string originalJson = await File.ReadAllTextAsync(filePath);
        Directory.CreateDirectory($"{filePath}.bak");

        progress.ClearedStageIds.Add(1);
        Exception? error = await Record.ExceptionAsync(() => store.UpsertAsync(progress, default));

        Assert.NotNull(error);
        Assert.Equal(originalJson, await File.ReadAllTextAsync(filePath));
        Assert.False(File.Exists($"{filePath}.tmp"));
    }

    private static (string FilePath, JsonFileProgressStore Store, DateTimeOffset Now) CreateStore()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "defensegame-server-tests", Guid.NewGuid().ToString("N"));
        var filePath = Path.Combine(tempDir, "progress.json");
        var now = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        return (filePath, new JsonFileProgressStore(filePath, new FakeClock(now)), now);
    }
}
