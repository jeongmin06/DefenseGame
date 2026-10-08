using Godot;

namespace DefenseGame.Client.Skills;

[GlobalClass]
public partial class CharacterSkillLoadout : RefCounted
{
    public string CharacterId { get; set; } = "";
    public SkillLoadout Loadout { get; set; } = null!;
}
