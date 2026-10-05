using Godot;

namespace DefenseGame.Client.Data;

// Generated data is shared read-only configuration; copy values into runtime state.
[GlobalClass]
public partial class WaveDefinition : Resource
{
    [Export] public UnitDefinition Enemy { get; set; } = null!;
    [Export] public int Count { get; set; }
    [Export] public float Interval { get; set; }
    [Export] public float HealthOverride { get; set; }
    [Export] public float SpeedOverride { get; set; }
}
