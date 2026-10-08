using DefenseGame.Client.Data;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DefenseGame.Client.Skills;

[GlobalClass]
public partial class StageSquadValidator : RefCounted
{
    public const int DefaultMaxSquadUnits = 10;
    public const int HardMaxSquadUnits = 10;

    public StageSquadValidationResult Validate(StageSquadAllocation allocation)
    {
        var result = new StageSquadValidationResult
        {
            SelectableCatCapacity = allocation.MaxSquadUnits - allocation.LegacyRosterUnitCount
        };
        if (allocation.MaxSquadUnits is < 1 or > HardMaxSquadUnits)
            Add(result, StageSquadError.InvalidMaxSquadUnits, "",
                $"Stage squad limit must be between 1 and {HardMaxSquadUnits}.");
        if (result.SelectableCatCapacity < 1)
            Add(result, StageSquadError.NoSelectableCapacity, "",
                "Stage squad limit must leave room for at least one selectable cat after legacy units.");
        if (allocation.CharacterIds.Count == 0)
            Add(result, StageSquadError.EmptySquad, "", "Select at least one cat.");
        if (allocation.CharacterIds.Count > Math.Max(0, result.SelectableCatCapacity))
            Add(result, StageSquadError.CapacityExceeded, "",
                $"Selected {allocation.CharacterIds.Count} cats for {Math.Max(0, result.SelectableCatCapacity)} available slots.");

        var profiles = allocation.OwnedProfiles.GroupBy(profile => profile.CharacterId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var selected = new List<CatProfile>();
        foreach (string characterId in allocation.CharacterIds)
        {
            if (!seen.Add(characterId))
            {
                Add(result, StageSquadError.DuplicateCharacter, characterId,
                    $"Character '{characterId}' appears more than once in the squad.");
                continue;
            }
            if (!profiles.TryGetValue(characterId, out CatProfile? profile))
            {
                Add(result, StageSquadError.MissingCharacter, characterId,
                    $"Character '{characterId}' is not owned.");
                continue;
            }
            if (profile.Unit is null || profile.Unit.Id != profile.UnitId)
            {
                Add(result, StageSquadError.MissingUnit, characterId,
                    $"Character '{characterId}' references missing or changed unit '{profile.UnitId}'.");
                continue;
            }
            if (profile.Unit.Role != UnitRole.Ranged)
            {
                Add(result, StageSquadError.UnsupportedRole, characterId,
                    $"Character '{characterId}' has unsupported selectable role '{profile.Unit.Role}'.");
                continue;
            }
            selected.Add(profile);
        }
        if (result.Issues.Count == 0)
            foreach (CatProfile profile in selected) result.SelectedProfiles.Add(profile);
        return result;
    }

    private static void Add(StageSquadValidationResult result, StageSquadError error, string characterId, string message) =>
        result.Issues.Add(new StageSquadValidationIssue { Error = error, CharacterId = characterId, Message = message });
}
