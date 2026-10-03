using System.Collections.Concurrent;
using DefenseGame.Server.Actors.Runtime;
using DefenseGame.Server.Services;

namespace DefenseGame.Server.Actors.Players;

public sealed class PlayerActorRegistry(IStageProgressService service, ActorThreadScheduler scheduler)
{
    private readonly ConcurrentDictionary<string, Lazy<PlayerActor>> _players = new(StringComparer.Ordinal);

    public PlayerActor Get(string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        var normalizedId = userId.Trim();
        return _players.GetOrAdd(normalizedId, id => new Lazy<PlayerActor>(
            () => new PlayerActor(id, service, scheduler.CreateChannel()))).Value;
    }
}
