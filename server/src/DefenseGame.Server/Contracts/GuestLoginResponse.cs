namespace DefenseGame.Server.Contracts;

public sealed record GuestLoginResponse(
    string PlayerId,
    string AccessToken,
    DateTimeOffset ExpiresAtUtc);
