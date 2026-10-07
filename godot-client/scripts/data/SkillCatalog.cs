using Godot;

namespace DefenseGame.Client.Data;

[GlobalClass]
public partial class SkillCatalog : Resource
{
    [Export] public Godot.Collections.Array<SkillDefinition> Skills { get; set; } = new();
}
