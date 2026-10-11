using Godot;

namespace DefenseGame.Client.Data;

// Generated data is shared read-only configuration; copy values into runtime state.
[GlobalClass]
public partial class StageDefinition : Resource
{
    [Export] public string Id { get; set; } = "";
    [Export] public string DisplayName { get; set; } = "";
    [Export] public LocalizedText Description { get; set; } = null!;
    [Export] public int MaxSquadUnits { get; set; } = 10;
    [Export] public int BaseHealth { get; set; }
    [Export] public float FirstWaveDelay { get; set; }
    [Export] public float WaveGap { get; set; }
    [Export] public int InitialDeploymentPoints { get; set; }
    [Export] public int MaxDeploymentPoints { get; set; }
    [Export] public float DeploymentPointRegenPerSecond { get; set; }
    [Export] public StageSkillBudget SkillBudget { get; set; } = null!;
    [Export] public GridDefinition Grid { get; set; } = null!;
    [Export] public Godot.Collections.Array<Vector2I> PathCorners { get; set; } = new();
    [Export] public Godot.Collections.Array<Vector2I> BlockedCells { get; set; } = new();
    [Export] public Godot.Collections.Array<RosterEntry> Roster { get; set; } = new();
    [Export] public Godot.Collections.Array<WaveDefinition> Waves { get; set; } = new();
}
