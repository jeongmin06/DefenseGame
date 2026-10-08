using DefenseGame.Client.Data;
using Godot;

namespace DefenseGame.Client.Skills;

public enum StageSquadPresetLoadStatus { Loaded, Defaults, RecoveredFromBackup, Corrupt, MigrationRequired }

[GlobalClass]
public partial class StageSquadPresetLoadResult : RefCounted
{
    public StageSquadPresetLoadStatus Status { get; set; }
    public string StatusCode => Status.ToString();
    public Godot.Collections.Array<StageSquadPreset> Presets { get; set; } = new();
    public string Message { get; set; } = "";
    public string PreservedPath { get; set; } = "";
    public bool RequiresExplicitOverwrite { get; set; }
}
