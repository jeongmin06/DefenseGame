using DefenseGame.Server.Contracts;

namespace DefenseGame.Server.Services;

public sealed class StageClearHandleResult
{
    private StageClearHandleResult(bool isAccepted, StageClearResponse? payload, string? errorCode, string? errorMessage)
    {
        IsAccepted = isAccepted;
        Payload = payload;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public bool IsAccepted { get; }
    public StageClearResponse? Payload { get; }
    public string? ErrorCode { get; }
    public string? ErrorMessage { get; }

    public static StageClearHandleResult Accepted(StageClearResponse payload) =>
        new(true, payload, null, null);

    public static StageClearHandleResult Rejected(string errorCode, string errorMessage) =>
        new(false, null, errorCode, errorMessage);
}
