using DefenseGame.Server.Actors.Runtime;
using Microsoft.Extensions.Configuration;

namespace DefenseGame.Server.Tests;

public sealed class ActorRuntimeTests
{
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Fact]
    public async Task SameActor_ExecutesInMailboxOrder_WithoutOverlap()
    {
        await using var runtime = await Runtime.StartAsync();
        var actor = runtime.CreateActor();
        var entered = Signal();
        var release = Signal();
        var order = new List<int>();
        var first = actor.Run(async token =>
        {
            entered.SetResult();
            await release.Task.WaitAsync(token);
            order.Add(1);
            return 1;
        });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = actor.Run(_ => { order.Add(2); return Task.FromResult(2); });
        var third = actor.Run(_ => { order.Add(3); return Task.FromResult(3); });
        Assert.False(second.IsCompleted);
        release.SetResult();
        await Task.WhenAll(first, second, third).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new[] { 1, 2, 3 }, order);
    }

    [Fact]
    public async Task DifferentActors_ContinueWhileAnotherActorIsAwaiting()
    {
        await using var runtime = await Runtime.StartAsync();
        var entered = Signal();
        var release = Signal();
        var first = runtime.CreateActor().Run(async token =>
        {
            entered.SetResult();
            await release.Task.WaitAsync(token);
            return 1;
        });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, await runtime.CreateActor().Run(_ => Task.FromResult(2)).WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(first.IsCompleted);
        release.SetResult();
        await first;
    }

    [Fact]
    public async Task MessageFailure_ReachesCaller_AndNextMessageRuns()
    {
        await using var runtime = await Runtime.StartAsync();
        var actor = runtime.CreateActor();
        await Assert.ThrowsAsync<InvalidOperationException>(() => actor.Run(_ => throw new InvalidOperationException("test"))
            .WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(2, await actor.Run(_ => Task.FromResult(2)).WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task QueuedCancellation_DoesNotExecuteAction_AndMailboxContinues()
    {
        await using var runtime = await Runtime.StartAsync();
        var actor = runtime.CreateActor();
        var entered = Signal();
        var release = Signal();
        var first = actor.Run(async token => { entered.SetResult(); await release.Task.WaitAsync(token); return 1; });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var cancellation = new CancellationTokenSource();
        var executed = false;
        var canceled = actor.Run(_ => { executed = true; return Task.FromResult(2); }, cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled.WaitAsync(TimeSpan.FromSeconds(5)));
        release.SetResult();
        await first;
        Assert.Equal(3, await actor.Run(_ => Task.FromResult(3)).WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(executed);
    }

    [Fact]
    public async Task RunningCancellation_DoesNotAllowNextMessageToOvertakeUnfinishedAction()
    {
        await using var runtime = await Runtime.StartAsync();
        var actor = runtime.CreateActor();
        var entered = Signal();
        var release = Signal();
        using var cancellation = new CancellationTokenSource();
        var first = actor.Run(async _ => { entered.SetResult(); await release.Task; return 1; }, cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first.WaitAsync(TimeSpan.FromSeconds(5)));
        var second = actor.Run(_ => Task.FromResult(2));
        Assert.False(second.IsCompleted);
        release.SetResult();
        Assert.Equal(2, await second.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Shutdown_CancelsRunningQueuedAndFutureCalls_AndJoinsWorkers()
    {
        await using var runtime = await Runtime.StartAsync();
        var actor = runtime.CreateActor();
        var entered = Signal();
        var first = actor.Run(async token => { entered.SetResult(); await Task.Delay(Timeout.Infinite, token); return 1; });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = actor.Run(_ => Task.FromResult(2));
        await runtime.Pool.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => actor.Run(_ => Task.FromResult(3)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runtime.CreateActor().Run(_ => Task.FromResult(4)));
    }

    [Fact]
    public async Task StoppingOneRuntime_DoesNotStopAnotherRuntime()
    {
        await using var first = await Runtime.StartAsync();
        await using var second = await Runtime.StartAsync();
        await first.Pool.StopAsync(CancellationToken.None);
        Assert.Equal(1, await second.CreateActor().Run(_ => Task.FromResult(1)).WaitAsync(TimeSpan.FromSeconds(5)));
    }

    private sealed class TestActor(ActorChannel channel) : Actor<TestActor>(channel)
    {
        public Task<int> Run(Func<CancellationToken, Task<int>> action, CancellationToken token = default) =>
            AskAsync((_, cancellation) => action(cancellation), token);
    }

    private sealed class Runtime : IAsyncDisposable
    {
        private readonly ActorThreadScheduler _scheduler = new();
        public ActorThreadPool Pool { get; }

        private Runtime()
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["Actors:WorkerCount"] = "2" }).Build();
            Pool = new ActorThreadPool(_scheduler, configuration);
        }

        public static async Task<Runtime> StartAsync()
        {
            var runtime = new Runtime();
            await runtime.Pool.StartAsync(CancellationToken.None);
            return runtime;
        }

        public TestActor CreateActor() => new(_scheduler.CreateChannel());

        public async ValueTask DisposeAsync()
        {
            await Pool.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
            _scheduler.Dispose();
        }
    }
}
