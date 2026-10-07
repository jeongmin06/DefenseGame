using DefenseGame.Client.Data;
using Godot;

namespace DefenseGame.Client.Skills;

// Runtime selection state. Generated SkillDefinition resources remain read-only.
[GlobalClass]
public partial class SkillLoadout : RefCounted
{
    public SkillDefinition ActiveSkill { get; set; } = null!;
    public Godot.Collections.Array<SkillDefinition> SupportSkills { get; set; } = new();
    public Godot.Collections.Array<SkillDefinition> SelectedSkills { get; set; } = new();
    public int TotalLinkCost { get; set; }
}
