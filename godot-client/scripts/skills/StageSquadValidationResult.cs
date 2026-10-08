using DefenseGame.Client.Data;
using Godot;

namespace DefenseGame.Client.Skills;

[GlobalClass]
public partial class StageSquadValidationResult : RefCounted
{
    public bool IsValid => Issues.Count == 0;
    public int SelectableCatCapacity { get; set; }
    public Godot.Collections.Array<StageSquadValidationIssue> Issues { get; set; } = new();
    public Godot.Collections.Array<CatProfile> SelectedProfiles { get; set; } = new();
}
