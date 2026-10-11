using System.Text.RegularExpressions;
using DefenseGame.Server.Contracts;
using DefenseGame.Server.Domain;
using DefenseGame.Server.Stores;

namespace DefenseGame.Server.Services;

public sealed partial class SkillProfileService(IProgressStore progressStore, ISystemClock clock) : ISkillProfileService
{
    private const int MaxSupportsPerCharacter = 5;

    public async Task<SkillProfileHandleResult> GetAsync(string userId, CancellationToken cancellationToken)
    {
        string normalizedUserId = userId?.Trim() ?? "";
        if (normalizedUserId.Length == 0)
            return SkillProfileHandleResult.Rejected("invalid_user_id", "userId is required.");

        UserProgress progress = await progressStore.GetOrCreateAsync(normalizedUserId, cancellationToken);
        return SkillProfileHandleResult.Accepted(ToResponse(progress));
    }

    public async Task<SkillProfileHandleResult> SaveAsync(
        string userId,
        SaveSkillProfileRequest request,
        CancellationToken cancellationToken)
    {
        string normalizedUserId = userId?.Trim() ?? "";
        if (normalizedUserId.Length == 0)
            return SkillProfileHandleResult.Rejected("invalid_user_id", "userId is required.");
        if (request.ExpectedRevision < 0)
            return SkillProfileHandleResult.Rejected("invalid_revision", "expectedRevision cannot be negative.");
        if (request.Presets is null)
            return SkillProfileHandleResult.Rejected("missing_presets", "presets is required.");

        UserProgress progress = await progressStore.GetOrCreateAsync(normalizedUserId, cancellationToken);
        if (request.ExpectedRevision != progress.SkillProfile.Revision)
            return SkillProfileHandleResult.Conflict(ToResponse(progress));
        if (request.IsMigration && progress.SkillProfile.Revision != 0)
            return SkillProfileHandleResult.Rejected("migration_already_completed",
                "Local migration is only allowed before the first server save.");

        if (!TryNormalizeAndValidate(progress, request.Presets, out Dictionary<string, SkillPresetState> presets,
                out string errorCode, out string message))
            return SkillProfileHandleResult.Rejected(errorCode, message);

        DateTimeOffset now = clock.UtcNow;
        foreach (SkillPresetState preset in presets.Values) preset.UpdatedAtUtc = now;
        progress.SkillProfile.Presets.Clear();
        foreach ((string characterId, SkillPresetState preset) in presets)
            progress.SkillProfile.Presets.Add(characterId, preset);
        progress.SkillProfile.Revision++;
        progress.SkillProfile.UpdatedAtUtc = now;
        progress.UpdatedAtUtc = now;
        UserProgress saved = await progressStore.UpsertAsync(progress, cancellationToken);
        return SkillProfileHandleResult.Accepted(ToResponse(saved));
    }

    private static bool TryNormalizeAndValidate(
        UserProgress progress,
        IReadOnlyList<SaveSkillPresetRequest> requests,
        out Dictionary<string, SkillPresetState> presets,
        out string errorCode,
        out string message)
    {
        presets = new Dictionary<string, SkillPresetState>(StringComparer.Ordinal);
        errorCode = "";
        message = "";
        if (requests.Count != progress.OwnedCharacterIds.Count)
            return Reject("preset_count_mismatch", "Provide exactly one preset for every owned character.", out errorCode, out message);

        int totalPoints = 0;
        foreach (SaveSkillPresetRequest request in requests)
        {
            string characterId = request.CharacterId?.Trim() ?? "";
            string activeSkillId = request.ActiveSkillId?.Trim() ?? "";
            if (!IdPattern().IsMatch(characterId) || !IdPattern().IsMatch(activeSkillId))
                return Reject("invalid_skill_profile_id", "Character and skill IDs must use lowercase snake case.", out errorCode, out message);
            if (!progress.OwnedCharacterIds.Contains(characterId))
                return Reject("character_not_owned", $"Character '{characterId}' is not owned.", out errorCode, out message);
            if (presets.ContainsKey(characterId))
                return Reject("duplicate_preset", $"Character '{characterId}' has more than one preset.", out errorCode, out message);
            if (!TryOwnedSkill(progress, activeSkillId, SkillRuleRole.Active, out SkillRule? active, out errorCode, out message))
                return false;
            if (!SkillCatalogRules.CharacterTags.TryGetValue(characterId, out IReadOnlySet<string>? characterTags)
                || active!.Tags.Any(tag => !characterTags.Contains(tag)))
                return Reject("unit_skill_mismatch", $"Character '{characterId}' cannot use '{activeSkillId}'.", out errorCode, out message);

            IReadOnlyList<string> supportRequests = request.SupportSkillIds ?? Array.Empty<string>();
            if (supportRequests.Count > MaxSupportsPerCharacter)
                return Reject("too_many_supports", $"Character '{characterId}' exceeds {MaxSupportsPerCharacter} supports.", out errorCode, out message);
            if (request.AllocatedPoints != supportRequests.Count)
                return Reject("allocated_point_mismatch", $"Character '{characterId}' allocatedPoints must equal support count.", out errorCode, out message);

            var supports = new List<string>(supportRequests.Count);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string? candidate in supportRequests)
            {
                string supportId = candidate?.Trim() ?? "";
                if (!IdPattern().IsMatch(supportId))
                    return Reject("invalid_skill_profile_id", $"Invalid support skill ID '{candidate}'.", out errorCode, out message);
                if (!seen.Add(supportId))
                    return Reject("duplicate_support", $"Support '{supportId}' is duplicated for '{characterId}'.", out errorCode, out message);
                if (!TryOwnedSkill(progress, supportId, SkillRuleRole.Support, out SkillRule? support, out errorCode, out message))
                    return false;
                if (!Supports(active.Tags, support!))
                    return Reject("incompatible_support", $"Support '{supportId}' is incompatible with '{activeSkillId}'.", out errorCode, out message);
                supports.Add(supportId);
            }

            totalPoints += request.AllocatedPoints;
            presets.Add(characterId, new SkillPresetState
            {
                CharacterId = characterId,
                ActiveSkillId = activeSkillId,
                SupportSkillIds = supports,
                AllocatedPoints = request.AllocatedPoints,
                UpdatedAtUtc = default
            });
        }

        foreach (string characterId in progress.OwnedCharacterIds)
            if (!presets.ContainsKey(characterId))
                return Reject("missing_preset", $"Character '{characterId}' has no preset.", out errorCode, out message);
        if (totalPoints > progress.SkillProfile.UnlockedPoints)
            return Reject("skill_budget_exceeded", $"Preset uses {totalPoints} of {progress.SkillProfile.UnlockedPoints} points.", out errorCode, out message);
        return true;
    }

    private static bool TryOwnedSkill(
        UserProgress progress,
        string skillId,
        SkillRuleRole expectedRole,
        out SkillRule? skill,
        out string errorCode,
        out string message)
    {
        if (!SkillCatalogRules.Skills.TryGetValue(skillId, out skill))
            return Reject("unknown_skill", $"Skill '{skillId}' does not exist.", out errorCode, out message);
        if (!progress.SkillProfile.OwnedSkillIds.Contains(skillId))
            return Reject("skill_not_owned", $"Skill '{skillId}' is not owned.", out errorCode, out message);
        if (skill.Role != expectedRole || skill.LinkCost != (expectedRole == SkillRuleRole.Active ? 0 : 1))
            return Reject("skill_role_mismatch", $"Skill '{skillId}' cannot be used in this slot.", out errorCode, out message);
        errorCode = "";
        message = "";
        return true;
    }

    private static bool Supports(IReadOnlySet<string> activeTags, SkillRule support) =>
        (support.RequiredAnyTags.Count == 0 || support.RequiredAnyTags.Any(activeTags.Contains))
        && support.RequiredAllTags.All(activeTags.Contains)
        && !support.ForbiddenTags.Any(activeTags.Contains);

    private static bool Reject(string code, string reason, out string errorCode, out string message)
    {
        errorCode = code;
        message = reason;
        return false;
    }

    private static SkillProfileResponse ToResponse(UserProgress progress) => new(
        progress.UserId,
        progress.SkillProfile.PlayerLevel,
        progress.SkillProfile.UnlockedPoints,
        progress.SkillProfile.OwnedSkillIds.Order(StringComparer.Ordinal).ToArray(),
        progress.SkillProfile.Presets.Values.OrderBy(preset => preset.CharacterId, StringComparer.Ordinal)
            .Select(preset => new SkillPresetResponse(
                preset.CharacterId,
                preset.ActiveSkillId,
                preset.SupportSkillIds.ToArray(),
                preset.AllocatedPoints,
                preset.UpdatedAtUtc))
            .ToArray(),
        progress.SkillProfile.Revision,
        progress.SkillProfile.UpdatedAtUtc);

    [GeneratedRegex("^[a-z][a-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();
}
