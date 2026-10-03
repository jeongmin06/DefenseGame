namespace DefenseGame.Server.Actors.Runtime;

public sealed class ActorThreadPool(ActorThreadScheduler scheduler, IConfiguration configuration) : IHostedService
{
    private Task _completion = Task.CompletedTask;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var workerCount = configuration.GetValue<int?>("Actors:WorkerCount") ?? Math.Max(2, Environment.ProcessorCount);
        if (workerCount < 1)
            throw new InvalidOperationException("Actors:WorkerCount must be positive.");
        var workers = Enumerable.Range(0, workerCount).Select(_ => new ActorThread(scheduler)).ToArray();
        _completion = Task.WhenAll(workers.Select(worker => worker.Completion));
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        scheduler.Complete();
        await _completion.WaitAsync(cancellationToken);
    }
}
