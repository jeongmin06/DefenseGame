using System.Text.RegularExpressions;
using DefenseGame.Server.Contracts;
using DefenseGame.Server.Domain;
using DefenseGame.Server.Stores;

namespace DefenseGame.Server.Services;

public sealed partial class StageSquadService(IProgressStore progressStore, ISystemClock clock) : IStageSquadService
{
    private static readonly IReadOnlyDictionary<string, int> StageLimits = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["stage_01"] = 10,
        ["stage_02"] = 10,
        ["stage_03"] = 10
    };

    public async Task<StageSquadHandleResult> GetAsync(
        string userId,
        string stageId,
        CancellationToken cancellationToken)
    {
        if (!TryValidateIdentity(userId, stageId, out string normalizedUserId, out string errorCode, out string message))
            return StageSquadHandleResult.Rejected(errorCode, message);

        UserProgress progress = await progressStore.GetOrCreateAsync(normalizedUserId, cancellationToken);
        return StageSquadHandleResult.Accepted(ToResponse(progress, stageId));
    }

    public async Task<StageSquadHandleResult> SaveAsync(
        string userId,
        string stageId,
        SaveStageSquadRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryValidateIdentity(userId, stageId, out string normalizedUserId, out string errorCode, out string message))
            return StageSquadHandleResult.Rejected(errorCode, message);
        if (request.ExpectedRevision < 0)
            return StageSquadHandleResult.Rejected("invalid_revision", "expectedRevision cannot be negative.");
        if (request.CharacterIds is null || request.CharacterIds.Count == 0)
            return StageSquadHandleResult.Rejected("empty_squad", "Select at least one character.");
        if (request.CharacterIds.Count > StageLimits[stageId])
            return StageSquadHandleResult.Rejected("squad_limit_exceeded", $"Stage squad limit is {StageLimits[stageId]}.");

        var normalizedCharacterIds = new List<string>(request.CharacterIds.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string? candidate in request.CharacterIds)
        {
            string characterId = candidate?.Trim() ?? "";
            if (!CharacterIdPattern().IsMatch(characterId))
                return StageSquadHandleResult.Rejected("invalid_character_id", $"Invalid characterId '{candidate}'.");
            if (!seen.Add(characterId))
                return StageSquadHandleResult.Rejected("duplicate_character", $"Character '{characterId}' appears more than once.");
            normalizedCharacterIds.Add(characterId);
        }

        UserProgress progress = await progressStore.GetOrCreateAsync(normalizedUserId, cancellationToken);
        string? missing = normalizedCharacterIds.FirstOrDefault(id => !progress.OwnedCharacterIds.Contains(id));
        if (missing is not null)
            return StageSquadHandleResult.Rejected("character_not_owned", $"Character '{missing}' is not owned.");

        int currentRevision = progress.StageSquads.TryGetValue(stageId, out StageSquadState? current)
            ? current.Revision : 0;
        if (request.ExpectedRevision != currentRevision)
            return StageSquadHandleResult.Conflict(ToResponse(progress, stageId));

        progress.StageSquads[stageId] = new StageSquadState
        {
            StageId = stageId,
            CharacterIds = normalizedCharacterIds,
            Revision = currentRevision + 1,
            UpdatedAtUtc = clock.UtcNow
        };
        progress.UpdatedAtUtc = clock.UtcNow;
        UserProgress saved = await progressStore.UpsertAsync(progress, cancellationToken);
        return StageSquadHandleResult.Accepted(ToResponse(saved, stageId));
    }

    private static bool TryValidateIdentity(
        string userId,
        string stageId,
        out string normalizedUserId,
        out string errorCode,
        out string message)
    {
        normalizedUserId = userId?.Trim() ?? "";
        if (normalizedUserId.Length == 0)
        {
            errorCode = "invalid_user_id";
            message = "userId is required.";
            return false;
        }
        if (!StageLimits.ContainsKey(stageId))
        {
            errorCode = "invalid_stage_id";
            message = $"Unknown stageId '{stageId}'.";
            return false;
        }
        errorCode = "";
        message = "";
        return true;
    }

    private static StageSquadResponse ToResponse(UserProgress progress, string stageId)
    {
        if (!progress.StageSquads.TryGetValue(stageId, out StageSquadState? squad))
            return new StageSquadResponse(progress.UserId, stageId, Array.Empty<string>(), 0, null);
        return new StageSquadResponse(
            progress.UserId,
            squad.StageId,
            squad.CharacterIds.ToArray(),
            squad.Revision,
            squad.UpdatedAtUtc);
    }

    [GeneratedRegex("^[a-z][a-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex CharacterIdPattern();
}
