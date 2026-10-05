using Godot;

namespace DefenseGame.Client.Data;

// Generated data is shared read-only configuration; copy values into runtime state.
[GlobalClass]
public partial class RosterEntry : Resource
{
    [Export] public UnitDefinition Unit { get; set; } = null!;
    [Export] public int Count { get; set; }
}
