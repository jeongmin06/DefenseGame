namespace DefenseGame.Server.Actors.Runtime;

public abstract class Actor<TActor>(ActorChannel channel) where TActor : Actor<TActor>
{
    public void Post(ActorMessage<TActor> message) => channel.Post(message);

    protected async Task<TResult> AskAsync<TResult>(
        Func<TActor, CancellationToken, Task<TResult>> action, CancellationToken cancellationToken)
    {
        TResult result = default!;
        var message = new ActorMessage<TActor>((TActor)this, async (actor, token) =>
        {
            result = await action(actor, token);
        }, cancellationToken, channel.StoppingToken);
        Post(message);
        await message.Completion;
        return result;
    }
}
