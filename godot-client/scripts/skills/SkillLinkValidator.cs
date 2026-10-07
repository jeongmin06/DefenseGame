using DefenseGame.Client.Data;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DefenseGame.Client.Skills;

// Pure link validation: no scene state, combat state, or Resource mutation.
[GlobalClass]
public partial class SkillLinkValidator : RefCounted
{
    public const int MaxSupportSkills = 3;
    public const int LinkCoreBudget = 3;

    public SkillLinkValidationResult Validate(
        SkillDefinition? activeSkill,
        Godot.Collections.Array<SkillDefinition> supportSkills)
    {
        if (activeSkill is null)
            return Failure(SkillLinkError.MissingActiveSkill, "An active skill is required.");
        if (activeSkill.Role != SkillRole.Active)
            return Failure(SkillLinkError.ActiveRoleRequired, $"Skill '{activeSkill.Id}' is not active.");
        if (activeSkill.LinkCost != 0)
            return Failure(SkillLinkError.InvalidActiveLinkCost, $"Active skill '{activeSkill.Id}' must cost 0 link cores.");
        if (supportSkills.Count > MaxSupportSkills)
            return Failure(SkillLinkError.TooManySupports, $"At most {MaxSupportSkills} support skills can be linked.");

        var activeTags = new HashSet<string>(activeSkill.Tags, StringComparer.Ordinal);
        var orderedSupports = supportSkills.OrderBy(skill => skill?.Id ?? "", StringComparer.Ordinal).ToArray();
        if (orderedSupports.Any(support => support is null))
            return Failure(SkillLinkError.MissingSupportSkill, "A support skill entry is missing.");

        int totalLinkCost = orderedSupports.Sum(support => support!.LinkCost);
        if (totalLinkCost > LinkCoreBudget)
            return Failure(SkillLinkError.LinkBudgetExceeded,
                $"Linked supports cost {totalLinkCost} cores, exceeding the budget of {LinkCoreBudget}.");

        var seenIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (SkillDefinition? support in orderedSupports)
        {
            if (support.Role != SkillRole.Support)
                return Failure(SkillLinkError.SupportRoleRequired, $"Skill '{support.Id}' is not a support skill.");
            if (support.LinkCost != 1)
                return Failure(SkillLinkError.InvalidSupportLinkCost, $"Support skill '{support.Id}' must cost 1 link core.");
            if (!seenIds.Add(support.Id))
                return Failure(SkillLinkError.DuplicateSupport, $"Support skill '{support.Id}' cannot be linked more than once.");

            if (support.RequiredAnyTags.Count > 0 && !support.RequiredAnyTags.Any(activeTags.Contains))
                return Failure(SkillLinkError.MissingAnyRequiredTag,
                    $"Support '{support.Id}' requires any of [{TagsText(support.RequiredAnyTags)}], but active '{activeSkill.Id}' does not have them.");

            string[] missingAll = support.RequiredAllTags.Where(tag => !activeTags.Contains(tag))
                .Order(StringComparer.Ordinal).ToArray();
            if (missingAll.Length > 0)
                return Failure(SkillLinkError.MissingAllRequiredTag,
                    $"Support '{support.Id}' requires all of [{string.Join(", ", missingAll)}], but active '{activeSkill.Id}' does not have them.");

            string[] forbidden = support.ForbiddenTags.Where(activeTags.Contains)
                .Order(StringComparer.Ordinal).ToArray();
            if (forbidden.Length > 0)
                return Failure(SkillLinkError.ForbiddenTag,
                    $"Support '{support.Id}' forbids active tag(s) [{string.Join(", ", forbidden)}].");
        }

        var loadout = new SkillLoadout
        {
            ActiveSkill = activeSkill,
            TotalLinkCost = totalLinkCost
        };
        loadout.SelectedSkills.Add(activeSkill);
        foreach (SkillDefinition support in orderedSupports)
        {
            loadout.SupportSkills.Add(support);
            loadout.SelectedSkills.Add(support);
        }

        return new SkillLinkValidationResult
        {
            IsValid = true,
            Error = SkillLinkError.None,
            Loadout = loadout
        };
    }

    private static SkillLinkValidationResult Failure(SkillLinkError error, string message) => new()
    {
        IsValid = false,
        Error = error,
        Message = message
    };

    private static string TagsText(IEnumerable<string> tags) =>
        string.Join(", ", tags.Order(StringComparer.Ordinal));
}
