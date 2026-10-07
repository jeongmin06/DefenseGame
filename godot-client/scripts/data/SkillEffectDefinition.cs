using Godot;

namespace DefenseGame.Client.Data;

public enum SkillEffectType
{
    AddProjectiles,
    AddPierce,
    AddFireDamage,
    MultiplyDamage,
    AddTag,
    MultiplyHealing
}

// Generated data is shared read-only configuration; copy values into runtime state.
[GlobalClass]
public partial class SkillEffectDefinition : Resource
{
    [Export] public SkillEffectType Type { get; set; }
    [Export] public int IntValue { get; set; }
    [Export] public float FloatValue { get; set; }
    [Export] public string TagValue { get; set; } = "";
}
