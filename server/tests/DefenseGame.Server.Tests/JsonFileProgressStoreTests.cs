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
    }
}
