using Godot;

namespace DefenseGame.Client.Data;

[GlobalClass]
public partial class UnitVisualDefinition : Resource
{
    [Export] public Texture2D SpriteSheet { get; set; } = null!;
    [Export] public Vector2I FrameSize { get; set; }
    [Export] public Vector2 SpritePosition { get; set; }
    [Export] public Vector2 SpriteScale { get; set; } = Vector2.One;
    [Export] public bool SourceFacesLeft { get; set; }
    [Export] public bool UseChromaKey { get; set; }
    [Export] public UnitAnimationDefinition? Idle { get; set; }
    [Export] public UnitAnimationDefinition? Move { get; set; }
    [Export] public UnitAnimationDefinition? Action { get; set; }
}
