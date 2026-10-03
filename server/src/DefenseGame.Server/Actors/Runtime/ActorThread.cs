namespace DefenseGame.Server.Actors.Runtime;

public sealed class ActorThread
{
    public ActorThread(ActorThreadScheduler scheduler)
    {
        Completion = RunLoopAsync(scheduler);
    }

    public Task Completion { get; }

    private static async Task RunLoopAsync(ActorThreadScheduler scheduler)
    {
        try
        {
            while (!scheduler.StoppingToken.IsCancellationRequested)
            {
                var channel = await scheduler.DequeueAsync();
                if (channel is null) break;
                await channel.RunAsync();
            }
        }
        catch (OperationCanceledException) when (scheduler.StoppingToken.IsCancellationRequested)
        {
        }
    }
}
