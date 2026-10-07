using Godot;

namespace DefenseGame.Client.Skills;

public enum SkillLinkError
{
    None,
    MissingActiveSkill,
    ActiveRoleRequired,
    InvalidActiveLinkCost,
    TooManySupports,
    MissingSupportSkill,
    SupportRoleRequired,
    InvalidSupportLinkCost,
    DuplicateSupport,
    MissingAnyRequiredTag,
    MissingAllRequiredTag,
    ForbiddenTag,
    LinkBudgetExceeded
}

[GlobalClass]
public partial class SkillLinkValidationResult : RefCounted
{
    public bool IsValid { get; set; }
    public SkillLinkError Error { get; set; }
    public string ErrorCode => Error.ToString();
    public string Message { get; set; } = "";
    public SkillLoadout? Loadout { get; set; }
}
