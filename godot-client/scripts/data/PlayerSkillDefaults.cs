using Godot;

namespace DefenseGame.Client.Data;

[GlobalClass]
public partial class PlayerSkillDefaults : Resource
{
    [Export] public PlayerSkillProgress Progress { get; set; } = null!;
    [Export] public Godot.Collections.Array<CatProfile> CatProfiles { get; set; } = new();
    [Export] public Godot.Collections.Array<CatSkillPreset> InitialPresets { get; set; } = new();
}
