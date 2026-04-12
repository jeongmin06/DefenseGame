namespace DefenseGame.Server.Services;

public interface ISystemClock
{
    DateTimeOffset UtcNow { get; }
}
