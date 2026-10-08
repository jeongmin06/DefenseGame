using DefenseGame.Client.Data;
using Godot;

namespace DefenseGame.Client.Skills;

[GlobalClass]
public partial class StageSquadAllocation : RefCounted
{
    public string StageId { get; set; } = "";
    public int MaxSquadUnits { get; set; } = 10;
    public int LegacyRosterUnitCount { get; set; }
    public int SelectableCatCapacity => MaxSquadUnits - LegacyRosterUnitCount;
    public Godot.Collections.Array<CatProfile> OwnedProfiles { get; set; } = new();
    public Godot.Collections.Array<string> CharacterIds { get; set; } = new();
}
