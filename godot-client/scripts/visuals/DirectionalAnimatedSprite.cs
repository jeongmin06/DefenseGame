using Godot;

namespace DefenseGame.Client.Visuals;

public partial class DirectionalAnimatedSprite : AnimatedSprite2D
{
    [Export]
    public bool SourceFacesLeft { get; set; }

    public void SetFacingLeft(bool facingLeft)
    {
        FlipH = facingLeft != SourceFacesLeft;
    }

    public void SetFacingFromMovement(Vector2 movement)
    {
        // Vertical movement and tiny horizontal drift preserve the last facing.
        if (Mathf.Abs(movement.X) > 0.001f)
        {
            SetFacingLeft(movement.X < 0.0f);
        }
    }
}
