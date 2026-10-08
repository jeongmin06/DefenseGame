using Godot;

namespace DefenseGame.Client.Data;

[GlobalClass]
public partial class PlayerSkillProgress : Resource
{
    [Export] public int PlayerLevel { get; set; }
    [Export] public int UnlockedPoints { get; set; }
    [Export] public Godot.Collections.Array<string> OwnedSkillIds { get; set; } = new();
}
