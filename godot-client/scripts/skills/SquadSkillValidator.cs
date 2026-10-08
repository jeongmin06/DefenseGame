using DefenseGame.Client.Data;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DefenseGame.Client.Skills;

[GlobalClass]
public partial class SquadSkillValidator : RefCounted
{
    public const int MaxSupportsPerCat = 5;

    public SquadSkillValidationResult Validate(
        SquadSkillAllocation allocation,
        PlayerSkillProgress progress,
        SkillCatalog skillCatalog)
    {
        var result = new SquadSkillValidationResult();
        int stageCap = Mathf.Max(0, allocation.StageCap);
        result.UsablePoints = allocation.HasStageCap
            ? Mathf.Min(Mathf.Max(0, progress.UnlockedPoints), stageCap)
            : Mathf.Max(0, progress.UnlockedPoints);

        if (allocation.PlayerUnlockedPoints != progress.UnlockedPoints)
            AddIssue(result, SquadSkillError.PlayerPointMismatch, "",
                $"Allocation has {allocation.PlayerUnlockedPoints} unlocked points, but player progress has {progress.UnlockedPoints}.");
        if (allocation.HasStageCap && allocation.StageCap < 0)
            AddIssue(result, SquadSkillError.InvalidStageCap, "", "Stage skill point cap cannot be negative.");

        var skills = skillCatalog.Skills.ToDictionary(skill => skill.Id, StringComparer.Ordinal);
        var owned = new HashSet<string>(progress.OwnedSkillIds, StringComparer.Ordinal);
        var profileGroups = allocation.CatProfiles.GroupBy(profile => profile.CharacterId, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal).ToArray();
        var presetGroups = allocation.Presets.GroupBy(preset => preset.CharacterId, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal).ToArray();
        var profileIds = new HashSet<string>(profileGroups.Select(group => group.Key), StringComparer.Ordinal);
        var presetsByCharacter = new Dictionary<string, CatSkillPreset>(StringComparer.Ordinal);

        foreach (var group in profileGroups)
            if (group.Count() > 1)
                AddIssue(result, SquadSkillError.DuplicateCharacter, group.Key,
                    $"Character '{group.Key}' appears more than once in the squad.");

        foreach (var group in presetGroups)
        {
            if (group.Count() > 1)
                AddIssue(result, SquadSkillError.DuplicatePreset, group.Key,
                    $"Character '{group.Key}' has more than one preset in the allocation.");
            else
                presetsByCharacter.Add(group.Key, group.Single());
            if (!profileIds.Contains(group.Key))
                AddIssue(result, SquadSkillError.MissingCharacter, group.Key,
                    $"Preset references character '{group.Key}', which is not in the squad.");
        }

        int calculatedTotal = 0;
        var validLoadouts = new List<CharacterSkillLoadout>();
        foreach (var group in profileGroups)
        {
            string characterId = group.Key;
            if (group.Count() != 1) continue;
            CatProfile profile = group.Single();
            if (!presetsByCharacter.TryGetValue(characterId, out CatSkillPreset? preset))
            {
                AddIssue(result, SquadSkillError.MissingPreset, characterId,
                    $"Character '{characterId}' has no skill preset in the allocation.");
                continue;
            }

            calculatedTotal += preset.AllocatedPoints;
            int issueStart = result.Issues.Count;
            ValidateProfileReference(result, profile);
            ValidatePreset(result, profile, preset, skills, owned, out SkillLoadout? loadout);
            if (result.Issues.Count == issueStart && loadout is not null)
                validLoadouts.Add(new CharacterSkillLoadout { CharacterId = characterId, Loadout = loadout });
        }

        if (allocation.TotalAllocatedPoints != calculatedTotal)
            AddIssue(result, SquadSkillError.TotalAllocatedPointMismatch, "",
                $"Allocation total is {allocation.TotalAllocatedPoints}, but cat presets total {calculatedTotal}.");
        if (allocation.TotalAllocatedPoints > result.UsablePoints)
            AddIssue(result, SquadSkillError.SquadBudgetExceeded, "",
                $"Allocation uses {allocation.TotalAllocatedPoints} points, exceeding {result.UsablePoints} usable points.");

        if (result.Issues.Count == 0)
            foreach (CharacterSkillLoadout entry in validLoadouts.OrderBy(entry => entry.CharacterId, StringComparer.Ordinal))
                result.CharacterLoadouts.Add(entry);
        return result;
    }

    private static void ValidateProfileReference(SquadSkillValidationResult result, CatProfile profile)
    {
        if (profile.Unit is null || profile.Unit.Id != profile.UnitId)
            AddIssue(result, SquadSkillError.MissingUnit, profile.CharacterId,
                $"Character '{profile.CharacterId}' references missing or changed unit '{profile.UnitId}'.");
    }

    private static void ValidatePreset(
        SquadSkillValidationResult result,
        CatProfile profile,
        CatSkillPreset preset,
        Dictionary<string, SkillDefinition> skills,
        HashSet<string> owned,
        out SkillLoadout? loadout)
    {
        loadout = null;
        if (preset.AllocatedPoints != preset.SupportSkillIds.Count)
            AddIssue(result, SquadSkillError.AllocatedPointMismatch, profile.CharacterId,
                $"Preset allocates {preset.AllocatedPoints} points for {preset.SupportSkillIds.Count} support skills.");
        if (preset.SupportSkillIds.Count > MaxSupportsPerCat)
            AddIssue(result, SquadSkillError.TooManySupports, profile.CharacterId,
                $"A cat can equip at most {MaxSupportsPerCat} support skills.");

        if (!TryOwnedSkill(result, profile.CharacterId, preset.ActiveSkillId, skills, owned, out SkillDefinition? active))
            return;
        if (active!.Role != SkillRole.Active)
        {
            AddIssue(result, SquadSkillError.ActiveRoleMismatch, profile.CharacterId,
                $"Skill '{active.Id}' is not an active skill.");
            return;
        }

        if (profile.Unit is not null)
        {
            var unitTags = new HashSet<string>(profile.Unit.SkillTags, StringComparer.Ordinal);
            string[] unsupported = active.Tags.Where(tag => !unitTags.Contains(tag)).Order(StringComparer.Ordinal).ToArray();
            if (unsupported.Length > 0)
                AddIssue(result, SquadSkillError.UnitSkillMismatch, profile.CharacterId,
                    $"Unit '{profile.UnitId}' does not support active tag(s) [{string.Join(", ", unsupported)}].");
        }

        var supports = new Godot.Collections.Array<SkillDefinition>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string supportId in preset.SupportSkillIds)
        {
            if (!seen.Add(supportId))
            {
                AddIssue(result, SquadSkillError.DuplicateSupport, profile.CharacterId,
                    $"Support skill '{supportId}' appears more than once.");
                continue;
            }
            if (!TryOwnedSkill(result, profile.CharacterId, supportId, skills, owned, out SkillDefinition? support))
                continue;
            if (support!.Role != SkillRole.Support)
            {
                AddIssue(result, SquadSkillError.SupportRoleMismatch, profile.CharacterId,
                    $"Skill '{support.Id}' is not a support skill.");
                continue;
            }
            supports.Add(support);
        }

        if (result.Issues.Any(issue => issue.CharacterId == profile.CharacterId)) return;
        SkillLinkValidationResult link = new SkillLinkValidator().ValidateWithLimits(
            active, supports, MaxSupportsPerCat, MaxSupportsPerCat);
        if (!link.IsValid || link.Loadout is null)
        {
            AddIssue(result, MapLinkError(link.Error), profile.CharacterId, link.Message);
            return;
        }
        loadout = link.Loadout;
    }

    private static bool TryOwnedSkill(
        SquadSkillValidationResult result,
        string characterId,
        string skillId,
        Dictionary<string, SkillDefinition> skills,
        HashSet<string> owned,
        out SkillDefinition? skill)
    {
        if (!skills.TryGetValue(skillId, out skill))
        {
            AddIssue(result, SquadSkillError.MissingSkill, characterId, $"Skill '{skillId}' does not exist.");
            return false;
        }
        if (!owned.Contains(skillId))
        {
            AddIssue(result, SquadSkillError.SkillNotOwned, characterId, $"Skill '{skillId}' is not owned.");
            return false;
        }
        return true;
    }

    private static SquadSkillError MapLinkError(SkillLinkError error) => error switch
    {
        SkillLinkError.TooManySupports => SquadSkillError.TooManySupports,
        SkillLinkError.DuplicateSupport => SquadSkillError.DuplicateSupport,
        SkillLinkError.ActiveRoleRequired => SquadSkillError.ActiveRoleMismatch,
        SkillLinkError.SupportRoleRequired => SquadSkillError.SupportRoleMismatch,
        SkillLinkError.InvalidActiveLinkCost or SkillLinkError.InvalidSupportLinkCost => SquadSkillError.InvalidSkillCost,
        SkillLinkError.MissingAnyRequiredTag or SkillLinkError.MissingAllRequiredTag => SquadSkillError.MissingRequiredTag,
        SkillLinkError.ForbiddenTag => SquadSkillError.ForbiddenTag,
        _ => SquadSkillError.InvalidSkillCost
    };

    private static void AddIssue(
        SquadSkillValidationResult result,
        SquadSkillError error,
        string characterId,
        string message) => result.Issues.Add(new SquadSkillValidationIssue
        {
            Error = error,
            CharacterId = characterId,
            Message = message
        });
}
