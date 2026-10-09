namespace DefenseGame.Server.Domain;

public sealed class StageSquadState
{
    public required string StageId { get; init; }
    public required List<string> CharacterIds { get; init; }
    public required int Revision { get; init; }
    public required DateTimeOffset UpdatedAtUtc { get; init; }

    public StageSquadState Clone() => new()
    {
        StageId = StageId,
        CharacterIds = new List<string>(CharacterIds),
        Revision = Revision,
        UpdatedAtUtc = UpdatedAtUtc
    };
}
