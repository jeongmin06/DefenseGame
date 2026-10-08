using Godot;

namespace DefenseGame.Client.Data;

[GlobalClass]
public partial class CatSkillPreset : Resource
{
    [Export] public string CharacterId { get; set; } = "";
    [Export] public string ActiveSkillId { get; set; } = "";
    [Export] public Godot.Collections.Array<string> SupportSkillIds { get; set; } = new();
    [Export] public int AllocatedPoints { get; set; }
    [Export] public string UpdatedAtUtc { get; set; } = "";
}
