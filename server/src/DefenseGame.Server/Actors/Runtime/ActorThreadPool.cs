namespace DefenseGame.Server.Actors.Runtime;

public sealed class ActorThreadPool(ActorThreadScheduler scheduler, IConfiguration configuration) : IHostedService
{
    private Task _completion = Task.CompletedTask;
    private int _isReady;

    public bool IsReady => Volatile.Read(ref _isReady) == 1;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var workerCount = configuration.GetValue<int?>("Actors:WorkerCount") ?? Math.Max(2, Environment.ProcessorCount);
        if (workerCount < 1)
            throw new InvalidOperationException("Actors:WorkerCount must be positive.");
        var workers = Enumerable.Range(0, workerCount).Select(_ => new ActorThread(scheduler)).ToArray();
        _completion = Task.WhenAll(workers.Select(worker => worker.Completion));
        Volatile.Write(ref _isReady, 1);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            scheduler.Complete();
            await _completion.WaitAsync(cancellationToken);
        }
        finally
        {
            Volatile.Write(ref _isReady, 0);
        }
    }
}
