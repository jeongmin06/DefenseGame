using DefenseGame.Server.Contracts;

namespace DefenseGame.Server.Services;

public enum StageSquadHandleStatus
{
    Accepted,
    Rejected,
    Conflict
}

public sealed class StageSquadHandleResult
{
    private StageSquadHandleResult(
        StageSquadHandleStatus status,
        StageSquadResponse? payload,
        string? errorCode,
        string? errorMessage)
    {
        Status = status;
        Payload = payload;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public StageSquadHandleStatus Status { get; }
    public StageSquadResponse? Payload { get; }
    public string? ErrorCode { get; }
    public string? ErrorMessage { get; }

    public static StageSquadHandleResult Accepted(StageSquadResponse payload) =>
        new(StageSquadHandleStatus.Accepted, payload, null, null);

    public static StageSquadHandleResult Rejected(string errorCode, string errorMessage) =>
        new(StageSquadHandleStatus.Rejected, null, errorCode, errorMessage);

    public static StageSquadHandleResult Conflict(StageSquadResponse current) =>
        new(StageSquadHandleStatus.Conflict, current, "revision_conflict", "The stage squad was changed by another request.");
}
