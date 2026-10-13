using DefenseGame.Client.Data;
using Godot;
using System.Collections.Generic;

namespace DefenseGame.Client.Combat;

public static class GridAttackRange
{
    public static Vector2I Rotate(Vector2I offset, UnitFacing facing) => facing switch
    {
        UnitFacing.Right => offset,
        UnitFacing.Down => new Vector2I(-offset.Y, offset.X),
        UnitFacing.Left => new Vector2I(-offset.X, -offset.Y),
        UnitFacing.Up => new Vector2I(offset.Y, -offset.X),
        _ => offset
    };

    public static bool Contains(
        Vector2I origin,
        Vector2I target,
        IEnumerable<Vector2I> canonicalOffsets,
        UnitFacing facing)
    {
        Vector2I relative = target - origin;
        foreach (Vector2I offset in canonicalOffsets)
            if (Rotate(offset, facing) == relative) return true;
        return false;
    }

    public static IEnumerable<Vector2I> Cells(
        Vector2I origin,
        IEnumerable<Vector2I> canonicalOffsets,
        UnitFacing facing)
    {
        foreach (Vector2I offset in canonicalOffsets)
            yield return origin + Rotate(offset, facing);
    }

    public static Vector2I DirectionVector(UnitFacing facing) => facing switch
    {
        UnitFacing.Right => Vector2I.Right,
        UnitFacing.Down => Vector2I.Down,
        UnitFacing.Left => Vector2I.Left,
        UnitFacing.Up => Vector2I.Up,
        _ => Vector2I.Right
    };
}
