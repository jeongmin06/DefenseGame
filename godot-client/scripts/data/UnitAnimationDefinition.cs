using Godot;

namespace DefenseGame.Client.Data;

[GlobalClass]
public partial class UnitAnimationDefinition : Resource
{
    [Export] public int StartFrame { get; set; }
    [Export] public int FrameCount { get; set; }
    [Export] public float FramesPerSecond { get; set; } = 1.0f;
    [Export] public bool Loop { get; set; }
}
