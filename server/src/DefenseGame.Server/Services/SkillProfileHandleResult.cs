using DefenseGame.Server.Contracts;

namespace DefenseGame.Server.Services;

public enum SkillProfileHandleStatus
{
    Accepted,
    Rejected,
    Conflict
}

public sealed record SkillProfileHandleResult(
    SkillProfileHandleStatus Status,
    SkillProfileResponse? Payload,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static SkillProfileHandleResult Accepted(SkillProfileResponse payload) =>
        new(SkillProfileHandleStatus.Accepted, payload, null, null);

    public static SkillProfileHandleResult Rejected(string errorCode, string message) =>
        new(SkillProfileHandleStatus.Rejected, null, errorCode, message);

    public static SkillProfileHandleResult Conflict(SkillProfileResponse current) =>
        new(SkillProfileHandleStatus.Conflict, current, "revision_conflict",
            "The skill profile was changed by another request.");
}
