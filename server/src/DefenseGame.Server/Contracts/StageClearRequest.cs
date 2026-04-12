namespace DefenseGame.Server.Contracts;

public sealed record StageClearRequest(
    string UserId,
    int StageId,
    bool WasVictory);
