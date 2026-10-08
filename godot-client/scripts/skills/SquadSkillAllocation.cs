using DefenseGame.Client.Data;
using Godot;

namespace DefenseGame.Client.Skills;

[GlobalClass]
public partial class SquadSkillAllocation : RefCounted
{
    public string StageId { get; set; } = "";
    public int PlayerUnlockedPoints { get; set; }
    public bool HasStageCap { get; set; }
    public int StageCap { get; set; }
    public int UsablePoints => HasStageCap
        ? Mathf.Min(Mathf.Max(0, PlayerUnlockedPoints), Mathf.Max(0, StageCap))
        : Mathf.Max(0, PlayerUnlockedPoints);
    public Godot.Collections.Array<CatProfile> CatProfiles { get; set; } = new();
    public Godot.Collections.Array<CatSkillPreset> Presets { get; set; } = new();
    public int TotalAllocatedPoints { get; set; }
}
