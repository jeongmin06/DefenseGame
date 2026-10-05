using Godot;

namespace DefenseGame.Client.Data;

// Generated data is shared read-only configuration; copy values into runtime state.
[GlobalClass]
public partial class GridDefinition : Resource
{
    [Export] public int Columns { get; set; }
    [Export] public int Rows { get; set; }
    [Export] public float CellSize { get; set; }
    [Export] public Vector2 Origin { get; set; }
}
