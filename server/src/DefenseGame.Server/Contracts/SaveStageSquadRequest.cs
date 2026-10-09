namespace DefenseGame.Server.Contracts;

public sealed record SaveStageSquadRequest(
    string UserId,
    IReadOnlyList<string>? CharacterIds,
    int ExpectedRevision);
