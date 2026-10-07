using Godot;

namespace DefenseGame.Client.Data;

public enum SkillRole
{
    Active,
    Support
}

// Generated data is shared read-only configuration; copy values into runtime state.
[GlobalClass]
public partial class SkillDefinition : Resource
{
    [Export] public string Id { get; set; } = "";
    [Export] public string DisplayName { get; set; } = "";
    [Export] public SkillRole Role { get; set; }
    [Export] public Godot.Collections.Array<string> Tags { get; set; } = new();
    [Export] public Godot.Collections.Array<string> RequiredAnyTags { get; set; } = new();
    [Export] public Godot.Collections.Array<string> RequiredAllTags { get; set; } = new();
    [Export] public Godot.Collections.Array<string> ForbiddenTags { get; set; } = new();
    [Export] public int LinkCost { get; set; }
    [Export] public int BaseProjectileCount { get; set; }
    [Export] public int BasePierceCount { get; set; }
    [Export] public float BaseDamageMultiplier { get; set; }
    [Export] public Godot.Collections.Array<SkillEffectDefinition> Effects { get; set; } = new();
}
