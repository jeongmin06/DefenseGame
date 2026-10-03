using System.Threading.Channels;

namespace DefenseGame.Server.Actors.Runtime;

public sealed class ActorThreadScheduler : IDisposable
{
    private readonly Channel<ActorChannel> _readyChannels = Channel.CreateUnbounded<ActorChannel>();
    private readonly CancellationTokenSource _stopping = new();
    private readonly List<ActorChannel> _channels = new();
    private readonly object _gate = new();
    private bool _completed;

    public CancellationToken StoppingToken => _stopping.Token;

    public ActorChannel CreateChannel()
    {
        lock (_gate)
        {
            var channel = new ActorChannel(this);
            if (_completed)
                channel.Close();
            else
                _channels.Add(channel);
            return channel;
        }
    }

    internal bool Schedule(ActorChannel channel) => _readyChannels.Writer.TryWrite(channel);

    internal async ValueTask<ActorChannel?> DequeueAsync()
    {
        try
        {
            return await _readyChannels.Reader.ReadAsync(StoppingToken);
        }
        catch (ChannelClosedException)
        {
            return null;
        }
    }

    public void Complete()
    {
        ActorChannel[] channels;
        lock (_gate)
        {
            if (_completed) return;
            _completed = true;
            _readyChannels.Writer.TryComplete();
            channels = _channels.ToArray();
            _channels.Clear();
        }
        _stopping.Cancel();
        foreach (var channel in channels)
            channel.Close();
    }

    public void Dispose()
    {
        Complete();
        _stopping.Dispose();
    }
}
