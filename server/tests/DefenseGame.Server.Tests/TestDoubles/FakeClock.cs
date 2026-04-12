using DefenseGame.Server.Services;

namespace DefenseGame.Server.Tests.TestDoubles;

internal sealed class FakeClock : ISystemClock
{
    public FakeClock(DateTimeOffset utcNow)
    {
        UtcNow = utcNow;
    }

    public DateTimeOffset UtcNow { get; set; }
}
