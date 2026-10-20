using Godot;
using DefenseGame.Client.Data;
using System.Collections.Generic;
using System.Linq;

namespace DefenseGame.Client.Combat;

public partial class DeploymentGrid : Node2D
{
    public enum TileType { Ground, EnemyPath, Blocked }
    public enum PlacementType { Ranged, Melee, Support }
    public enum PlacementBlockReason
    {
        None,
        PlacementDisabled,
        OutsideGrid,
        Occupied,
        BlockedTerrain,
        TerrainNotAllowed
    }

    public int Columns { get; private set; }
    public int Rows { get; private set; }
    public float CellSize { get; private set; }

    [Signal]
    public delegate void CellSelectedEventHandler(Vector2I cell);
    [Signal]
    public delegate void FacingSelectedEventHandler(int facing);
    [Signal]
    public delegate void FacingSelectionCanceledEventHandler();

    private TileType[,] _tiles = new TileType[0, 0];
    private readonly Dictionary<PlacementType, PlacementRule> _rules = new();
    private readonly HashSet<Vector2I> _occupied = new();
    public PlacementType SelectedProfile { get; private set; } = PlacementType.Ranged;

    public void SetPlacementType(PlacementType profile)
    {
        SelectedProfile = profile;
        QueueRedraw();
    }

    private bool _placementEnabled = true;
    public bool PlacementEnabled => _placementEnabled;
    public bool IsChoosingFacing => _isChoosingFacing;
    public Vector2I FacingOrigin => _facingOrigin;
    public UnitFacing PreviewFacing => _previewFacing;
    public Godot.Collections.Array<Vector2I> PreviewRangeCells => new(
        GridAttackRange.Cells(_facingOrigin, _facingOffsets, _previewFacing).Where(ContainsCell));
    private Vector2I _hovered = new(-1, -1);
    private bool _isChoosingFacing;
    private Vector2I _facingOrigin = new(-1, -1);
    private Vector2I[] _facingOffsets = [];
    private UnitFacing _previewFacing = UnitFacing.Right;

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
        return _rules.TryGetValue(profile, out PlacementRule rule) && rule switch
        {
            PlacementRule.Ground => tile == TileType.Ground,
            PlacementRule.GroundOrPath => tile == TileType.Ground || tile == TileType.EnemyPath,
            _ => false
        };
    }

    public PlacementBlockReason GetPlacementBlockReason(Vector2I cell, PlacementType profile)
    {
        if (!_placementEnabled || _isChoosingFacing) return PlacementBlockReason.PlacementDisabled;
        if (!ContainsCell(cell)) return PlacementBlockReason.OutsideGrid;
        if (_occupied.Contains(cell)) return PlacementBlockReason.Occupied;
        TileType tile = GetTileType(cell);
        if (tile == TileType.Blocked) return PlacementBlockReason.BlockedTerrain;
        return CanPlace(cell, profile) ? PlacementBlockReason.None : PlacementBlockReason.TerrainNotAllowed;
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
        if (!enabled) CancelFacingSelection();
        QueueRedraw();
    }

    // Selection is a request; the stage owns the unit count and commits occupancy.
    public bool SelectCell(Vector2I cell)
    {
        if (_isChoosingFacing || !_placementEnabled || !CanPlace(cell, SelectedProfile)) return false;
        EmitSignal(SignalName.CellSelected, cell);
        return true;
    }

    public bool BeginFacingSelection(Vector2I cell, IEnumerable<Vector2I> canonicalOffsets)
    {
        if (!_placementEnabled || !CanPlace(cell, SelectedProfile)) return false;
        Vector2I[] offsets = canonicalOffsets.Distinct().ToArray();
        if (offsets.Length == 0) return false;
        _isChoosingFacing = true;
        _facingOrigin = cell;
        _facingOffsets = offsets;
        _previewFacing = UnitFacing.Right;
        QueueRedraw();
        return true;
    }

    public void SetPreviewFacing(UnitFacing facing)
    {
        if (!_isChoosingFacing) return;
        _previewFacing = facing;
        QueueRedraw();
    }

    public bool SelectFacing(UnitFacing facing)
    {
        if (!_isChoosingFacing) return false;
        _previewFacing = facing;
        _isChoosingFacing = false;
        _facingOffsets = [];
        QueueRedraw();
        EmitSignal(SignalName.FacingSelected, (int)facing);
        return true;
    }

    public bool CancelFacingSelection()
    {
        if (!_isChoosingFacing) return false;
        _isChoosingFacing = false;
        _facingOrigin = new Vector2I(-1, -1);
        _facingOffsets = [];
        QueueRedraw();
        EmitSignal(SignalName.FacingSelectionCanceled);
        return true;
    }

    public void Configure(StageDefinition definition)
    {
        Columns = definition.Grid.Columns;
        Rows = definition.Grid.Rows;
        CellSize = definition.Grid.CellSize;
        Position = definition.Grid.Origin;
        _tiles = new TileType[Columns, Rows];
        _rules.Clear();
        foreach (RosterEntry entry in definition.Roster)
            _rules[(PlacementType)entry.Unit.Role] = entry.Unit.Placement;
        var pathCorners = new Godot.Collections.Array<Vector2I>(definition.PathCorners);
        var blockedCells = new Godot.Collections.Array<Vector2I>(definition.BlockedCells);
        _occupied.Clear();
        _isChoosingFacing = false;
        _facingOffsets = [];
        _placementEnabled = true;
        foreach (Vector2I cell in blockedCells)
        {
            if (ContainsCell(cell)) _tiles[cell.X, cell.Y] = TileType.Blocked;
        }
        for (int i = 1; i < pathCorners.Count; i++)
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
        if (_isChoosingFacing)
        {
            if (input is InputEventMouseMotion motion)
            {
                SetPreviewFacing(FacingFromPosition(motion.GlobalPosition));
                return;
            }
            if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
            {
                SelectFacing(FacingFromPosition(GetGlobalMousePosition()));
                GetViewport().SetInputAsHandled();
                return;
            }
            if (input is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true }
                || input is InputEventKey { Keycode: Key.Escape, Pressed: true })
            {
                CancelFacingSelection();
                GetViewport().SetInputAsHandled();
                return;
            }
        }
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
        var previewCells = _isChoosingFacing
            ? new HashSet<Vector2I>(GridAttackRange.Cells(_facingOrigin, _facingOffsets, _previewFacing).Where(ContainsCell))
            : new HashSet<Vector2I>();
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
            if (previewCells.Contains(cell))
                DrawRect(rect.Grow(-5), new Color(0.35f, 0.88f, 0.82f, 0.32f), true);
            if (_isChoosingFacing && cell == _facingOrigin)
                DrawRect(rect.Grow(-3), new Color(1.0f, 0.75f, 0.25f, 0.85f), false, 4);
            DrawRect(rect, new Color(0.15f, 0.22f, 0.1f, 0.25f), false, 1);
            if (_placementEnabled && !_isChoosingFacing && cell == _hovered)
            {
                Color highlight = CanPlace(cell, SelectedProfile)
                    ? new Color(1, 0.92f, 0.5f, 0.7f) : new Color(1, 0.3f, 0.2f, 0.6f);
                DrawRect(rect.Grow(-2), highlight, false, 3);
            }
        }
        if (_isChoosingFacing)
        {
            Vector2 center = CellCenter(_facingOrigin);
            Vector2 direction = (Vector2)GridAttackRange.DirectionVector(_previewFacing);
            Vector2 tip = center + direction * CellSize * 0.42f;
            DrawLine(center, tip, new Color(1.0f, 0.82f, 0.35f), 6, true);
            DrawCircle(tip, 8, new Color(1.0f, 0.82f, 0.35f));
        }
    }

    private UnitFacing FacingFromPosition(Vector2 globalPosition)
    {
        Vector2 delta = globalPosition - CellToGlobal(_facingOrigin);
        if (Mathf.Abs(delta.X) >= Mathf.Abs(delta.Y))
            return delta.X < 0 ? UnitFacing.Left : UnitFacing.Right;
        return delta.Y < 0 ? UnitFacing.Up : UnitFacing.Down;
    }
}
