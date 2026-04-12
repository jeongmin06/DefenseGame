namespace DefenseGame.Server.Contracts;

public sealed record RewardDto(
    string RewardId,
    int Amount,
    string Reason);
