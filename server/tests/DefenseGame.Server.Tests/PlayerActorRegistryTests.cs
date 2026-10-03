using DefenseGame.Server.Actors.Players;
using DefenseGame.Server.Actors.Runtime;
using DefenseGame.Server.Contracts;
using DefenseGame.Server.Services;
using Microsoft.Extensions.Configuration;

namespace DefenseGame.Server.Tests;

public sealed class PlayerActorRegistryTests
{
    [Fact]
    public async Task ConcurrentLookups_ReturnOneActorPerNormalizedUser()
    {
        using var scheduler = new ActorThreadScheduler();
        var registry = new PlayerActorRegistry(new ControlledService(), scheduler);
        var actors = await Task.WhenAll(Enumerable.Range(0, 100).Select(index =>
            Task.Run(() => registry.Get(index % 2 == 0 ? " user " : "user"))));
        Assert.All(actors, actor => Assert.Same(actors[0], actor));
        Assert.NotSame(actors[0], registry.Get("another-user"));
    }

    [Fact]
    public async Task DifferentPlayers_AreIndependent_AndQueriesWaitForSamePlayerCommands()
    {
        using var scheduler = new ActorThreadScheduler();
        var pool = new ActorThreadPool(scheduler, new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Actors:WorkerCount"] = "2" }).Build());
        await pool.StartAsync(CancellationToken.None);
        try
        {
            var service = new ControlledService();
            var registry = new PlayerActorRegistry(service, scheduler);
            var clear = registry.Get("first").HandleStageClearAsync(new StageClearRequest("first", 1, true), default);
            await service.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var samePlayerQuery = registry.Get(" first ").GetProgressAsync(default);
            var otherPlayerQuery = await registry.Get("second").GetProgressAsync(default).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("second", otherPlayerQuery.UserId);
            Assert.False(samePlayerQuery.IsCompleted);
            service.Release.SetResult();
            await clear.WaitAsync(TimeSpan.FromSeconds(5));
            var progress = await samePlayerQuery.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, progress.HighestClearedStageId);
        }
        finally
        {
            await pool.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private sealed class ControlledService : IStageProgressService
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _cleared;

        public Task<ProgressResponse> GetProgressAsync(string userId, CancellationToken cancellationToken) =>
            Task.FromResult(new ProgressResponse(userId, _cleared ? 1 : 0, _cleared ? 2 : 1,
                _cleared ? new[] { 1 } : Array.Empty<int>(), DateTimeOffset.UtcNow));

        public async Task<StageClearHandleResult> HandleStageClearAsync(StageClearRequest request, CancellationToken cancellationToken)
        {
            Entered.SetResult();
            await Release.Task.WaitAsync(cancellationToken);
            _cleared = true;
            return StageClearHandleResult.Accepted(new StageClearResponse(request.UserId, 1, true, 1, 2,
                Array.Empty<RewardDto>(), DateTimeOffset.UtcNow));
        }
    }
}
