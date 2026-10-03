using System.Threading.Channels;

namespace DefenseGame.Server.Actors.Runtime;

public sealed class ActorChannel(ActorThreadScheduler scheduler)
{
    private readonly Channel<IActorMessage> _messages = Channel.CreateUnbounded<IActorMessage>(
        new UnboundedChannelOptions { SingleReader = false, SingleWriter = false });
    private int _scheduled;

    public CancellationToken StoppingToken => scheduler.StoppingToken;

    public void Post(IActorMessage message)
    {
        if (!_messages.Writer.TryWrite(message))
        {
            message.Cancel();
            return;
        }
        Schedule();
    }

    private void Schedule()
    {
        if (Interlocked.Exchange(ref _scheduled, 1) == 0 && !scheduler.Schedule(this))
        {
            Close();
        }
    }

    internal async ValueTask RunAsync()
    {
        // One message per turn prevents a busy player's mailbox starving other players.
        if (_messages.Reader.TryRead(out var message))
        {
            try
            {
                await message.RunAsync();
            }
            catch (Exception exception)
            {
                message.Fail(exception);
            }
        }
        Interlocked.Exchange(ref _scheduled, 0);
        if (_messages.Reader.TryPeek(out _))
        {
            Schedule();
        }
    }

    internal void Close()
    {
        _messages.Writer.TryComplete();
        while (_messages.Reader.TryRead(out var message))
        {
            message.Cancel();
        }
    }
}
