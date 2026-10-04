using Godot;
using System.Collections.Generic;

namespace DefenseGame.Client.Combat;

public partial class DeploymentGrid : Node2D
{
    public enum TileType { Ground, EnemyPath, Blocked }
    public enum PlacementType { Ranged, Melee, Support }

    public const int Columns = 16;
    public const int Rows = 8;
    public const float CellSize = 75.0f;

    [Signal]
    public delegate void CellSelectedEventHandler(Vector2I cell);

    private readonly TileType[,] _tiles = new TileType[Columns, Rows];
    private readonly HashSet<Vector2I> _occupied = new();
    public PlacementType SelectedProfile { get; private set; } = PlacementType.Ranged;

    public void SetPlacementType(PlacementType profile)
    {
        SelectedProfile = profile;
        QueueRedraw();
    }

    private bool _placementEnabled = true;
    private Vector2I _hovered = new(-1, -1);

    public bool ContainsCell(Vector2I cell) => cell.X >= 0 && cell.X < Columns && cell.Y >= 0 && cell.Y < Rows;

    public Vector2 CellCenter(Vector2I cell) => new((cell.X + 0.5f) * CellSize, (cell.Y + 0.5f) * CellSize);

    public Vector2 CellToGlobal(Vector2I cell) => ToGlobal(CellCenter(cell));

    public Vector2I GlobalToCell(Vector2 position)
    {
        Vector2 local = ToLocal(position);
        return new Vector2I(Mathf.FloorToInt(local.X / CellSize), Mathf.FloorToInt(local.Y / CellSize));
    }

    public TileType GetTileType(Vector2I cell) => ContainsCell(cell) ? _tiles[cell.X, cell.Y] : TileType.Blocked;

    public bool CanPlace(Vector2I cell, PlacementType profile)
    {
        if (!ContainsCell(cell) || _occupied.Contains(cell)) return false;
        TileType tile = GetTileType(cell);
        return profile switch
        {
            PlacementType.Ranged or PlacementType.Support => tile == TileType.Ground,
            PlacementType.Melee => tile == TileType.Ground || tile == TileType.EnemyPath,
            _ => false
        };
    }

    public bool TryOccupy(Vector2I cell, PlacementType profile)
    {
        if (!_placementEnabled || !CanPlace(cell, profile)) return false;
        _occupied.Add(cell);
        QueueRedraw();
        return true;
    }

    public void ReleaseCell(Vector2I cell)
    {
        if (_occupied.Remove(cell)) QueueRedraw();
    }

    public void SetPlacementEnabled(bool enabled)
    {
        _placementEnabled = enabled;
        QueueRedraw();
    }

    // Selection is a request; the stage owns the unit count and commits occupancy.
    public bool SelectCell(Vector2I cell)
    {
        if (!_placementEnabled || !CanPlace(cell, SelectedProfile)) return false;
        EmitSignal(SignalName.CellSelected, cell);
        return true;
    }

    public void Configure(Vector2I[] pathCorners, Vector2I[] blockedCells)
    {
        System.Array.Clear(_tiles);
        _occupied.Clear();
        _placementEnabled = true;
        foreach (Vector2I cell in blockedCells)
        {
            if (ContainsCell(cell)) _tiles[cell.X, cell.Y] = TileType.Blocked;
        }
        for (int i = 1; i < pathCorners.Length; i++)
        {
            Vector2I start = pathCorners[i - 1];
            Vector2I end = pathCorners[i];
            if (start.X != end.X && start.Y != end.Y)
                throw new System.ArgumentException("Path segments must be horizontal or vertical.");
            Vector2I step = new(System.Math.Sign(end.X - start.X), System.Math.Sign(end.Y - start.Y));
            for (Vector2I cell = start; ; cell += step)
            {
                if (ContainsCell(cell))
                {
                    if (GetTileType(cell) == TileType.Blocked)
                        throw new System.ArgumentException("Enemy paths cannot cross blocked cells.");
                    _tiles[cell.X, cell.Y] = TileType.EnemyPath;
                }
                if (cell == end) break;
            }
        }
        QueueRedraw();
    }

    public override void _UnhandledInput(InputEvent input)
    {
        if (input is InputEventMouseMotion)
        {
            Vector2I cell = GlobalToCell(GetGlobalMousePosition());
            if (cell != _hovered)
            {
                _hovered = cell;
                QueueRedraw();
            }
        }
        if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }
            && SelectCell(GlobalToCell(GetGlobalMousePosition())))
            GetViewport().SetInputAsHandled();
    }

    public override void _Draw()
    {
        for (int y = 0; y < Rows; y++)
        for (int x = 0; x < Columns; x++)
        {
            Vector2I cell = new(x, y);
            Rect2 rect = new(new Vector2(x * CellSize, y * CellSize), new Vector2(CellSize, CellSize));
            TileType tile = GetTileType(cell);
            Color color = tile switch
            {
                TileType.EnemyPath => new Color("ba8f5b"),
                TileType.Blocked => new Color("626e56"),
                _ => (x + y) % 2 == 0 ? new Color("608b42") : new Color("5d873f")
            };
            DrawRect(rect, color);
            if (tile == TileType.Ground)
            {
                Vector2 detail = rect.Position + new Vector2(16 + x % 3 * 7, 40);
                DrawLine(detail, detail + new Vector2(-3, -7), new Color("70974c"), 2);
                DrawLine(detail, detail + new Vector2(3, -9), new Color("70974c"), 2);
            }
            if (_placementEnabled && CanPlace(cell, SelectedProfile))
                DrawRect(rect.Grow(-4), new Color(0.85f, 0.94f, 0.6f, 0.16f), false, 1);
            if (_occupied.Contains(cell))
                DrawRect(rect.Grow(-5), new Color(1, 0.85f, 0.5f, 0.35f), false, 2);
            DrawRect(rect, new Color(0.15f, 0.22f, 0.1f, 0.25f), false, 1);
            if (_placementEnabled && cell == _hovered)
            {
                Color highlight = CanPlace(cell, SelectedProfile)
                    ? new Color(1, 0.92f, 0.5f, 0.7f) : new Color(1, 0.3f, 0.2f, 0.6f);
                DrawRect(rect.Grow(-2), highlight, false, 3);
            }
        }
    }
}
