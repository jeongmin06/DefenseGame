using Godot;

namespace DefenseGame.Client.Data;

public enum UnitRole { Ranged, Melee, Support, Enemy }
public enum PlacementRule { Ground, GroundOrPath, None }

// Generated data is shared read-only configuration; copy values into runtime state.
[GlobalClass]
public partial class UnitDefinition : Resource
{
    [Export] public string Id { get; set; } = "";
    [Export] public string DisplayName { get; set; } = "";
    [Export] public UnitRole Role { get; set; }
    [Export] public PlacementRule Placement { get; set; }
    [Export] public PackedScene Scene { get; set; } = null!;
    [Export] public float MaxHealth { get; set; }
    [Export] public float ActionPower { get; set; }
    [Export] public float ActionInterval { get; set; }
    [Export] public int ActionFrame { get; set; }
    [Export] public int TargetLimit { get; set; }
    [Export] public float RangePixels { get; set; }
    [Export] public int RangeCells { get; set; }
    [Export] public Godot.Collections.Array<Vector2I> AttackCellOffsets { get; set; } = new();
    [Export] public int BlockCount { get; set; }
    [Export] public PackedScene? ProjectileScene { get; set; }
    [Export] public float ProjectileSpeed { get; set; }
    [Export] public float ProjectileHitDistance { get; set; }
    [Export] public Vector2 ProjectileSpawnOffset { get; set; }
    [Export] public float MoveSpeed { get; set; }
}
