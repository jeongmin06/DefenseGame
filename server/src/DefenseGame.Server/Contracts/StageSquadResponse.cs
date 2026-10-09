namespace DefenseGame.Server.Contracts;

public sealed record StageSquadResponse(
    string UserId,
    string StageId,
    IReadOnlyList<string> CharacterIds,
    int Revision,
    DateTimeOffset? UpdatedAtUtc);
