using Godot;

namespace DefenseGame.Client.Combat;

public partial class TowerSlot : Node2D
{
    [Signal]
    public delegate void SelectedEventHandler(TowerSlot slot);

    public bool IsOccupied { get; private set; }
    private bool _placementEnabled = true;
    private Area2D _clickArea = null!;

    public override void _Ready()
    {
        _clickArea = GetNode<Area2D>("ClickArea");
        _clickArea.InputEvent += OnInputEvent;
    }

    public void SetPlacementEnabled(bool enabled)
    {
        _placementEnabled = enabled;
        _clickArea.InputPickable = enabled && !IsOccupied;
        QueueRedraw();
    }

    public void MarkOccupied()
    {
        IsOccupied = true;
        SetPlacementEnabled(false);
    }

    public void Select()
    {
        if (_placementEnabled && !IsOccupied)
        {
            EmitSignal(SignalName.Selected, this);
        }
    }

    private void OnInputEvent(Node viewport, InputEvent input, long shapeIndex)
    {
        if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
        {
            Select();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Draw()
    {
        float alpha = IsOccupied ? 0.25f : _placementEnabled ? 1.0f : 0.4f;
        DrawCircle(Vector2.Zero, 28.0f, new Color(0.15f, 0.23f, 0.12f, alpha * 0.75f));
        DrawArc(Vector2.Zero, 28.0f, 0, Mathf.Tau, 48, new Color(1.0f, 0.83f, 0.43f, alpha), 3.0f);
        if (!IsOccupied)
        {
            Color mark = new(1.0f, 0.91f, 0.65f, alpha);
            DrawLine(new Vector2(-8, 0), new Vector2(8, 0), mark, 3.0f);
            DrawLine(new Vector2(0, -8), new Vector2(0, 8), mark, 3.0f);
        }
    }
}
