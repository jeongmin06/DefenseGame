using Godot;

namespace DefenseGame.Client.Skills;

public enum StageSquadError
{
    None,
    InvalidMaxSquadUnits,
    NoSelectableCapacity,
    EmptySquad,
    CapacityExceeded,
    DuplicateCharacter,
    MissingCharacter,
    MissingUnit,
    UnsupportedRole
}

[GlobalClass]
public partial class StageSquadValidationIssue : RefCounted
{
    public StageSquadError Error { get; set; }
    public string ErrorCode => Error.ToString();
    public string CharacterId { get; set; } = "";
    public string Message { get; set; } = "";
}
