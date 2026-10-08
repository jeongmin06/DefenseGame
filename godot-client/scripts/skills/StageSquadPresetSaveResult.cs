using Godot;

namespace DefenseGame.Client.Skills;

public enum StageSquadPresetSaveStatus { Saved, Failed, BlockedProtectedData, MigrationRequired }

[GlobalClass]
public partial class StageSquadPresetSaveResult : RefCounted
{
    public StageSquadPresetSaveStatus Status { get; set; }
    public string StatusCode => Status.ToString();
    public string Message { get; set; } = "";
}
