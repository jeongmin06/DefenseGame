using DefenseGame.Client.Data;
using Godot;

namespace DefenseGame.Client.Skills;

public enum SkillProfileLoadStatus
{
    Loaded,
    Defaults,
    RecoveredFromBackup,
    Corrupt,
    MigrationRequired
}

[GlobalClass]
public partial class SkillProfileLoadResult : RefCounted
{
    public SkillProfileLoadStatus Status { get; set; }
    public string StatusCode => Status.ToString();
    public PlayerSkillProgress Progress { get; set; } = null!;
    public Godot.Collections.Array<CatSkillPreset> Presets { get; set; } = new();
    public string Message { get; set; } = "";
    public string PreservedPath { get; set; } = "";
    public bool RequiresExplicitOverwrite { get; set; }
}
