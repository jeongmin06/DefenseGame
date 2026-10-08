using Godot;

namespace DefenseGame.Client.Skills;

public enum SquadSkillError
{
    None,
    PlayerPointMismatch,
    InvalidStageCap,
    DuplicateCharacter,
    MissingCharacter,
    MissingUnit,
    MissingPreset,
    DuplicatePreset,
    AllocatedPointMismatch,
    TooManySupports,
    MissingSkill,
    SkillNotOwned,
    ActiveRoleMismatch,
    SupportRoleMismatch,
    InvalidSkillCost,
    DuplicateSupport,
    MissingRequiredTag,
    ForbiddenTag,
    UnitSkillMismatch,
    TotalAllocatedPointMismatch,
    SquadBudgetExceeded
}

[GlobalClass]
public partial class SquadSkillValidationIssue : RefCounted
{
    public SquadSkillError Error { get; set; }
    public string ErrorCode => Error.ToString();
    public string CharacterId { get; set; } = "";
    public string Message { get; set; } = "";
}
