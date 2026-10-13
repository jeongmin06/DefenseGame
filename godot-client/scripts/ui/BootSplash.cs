using Godot;

namespace DefenseGame.Client.UI;

public partial class BootSplash : Node
{
    public override void _Ready() => Callable.From(() => LoadingScreen.ShowBoot(this)).CallDeferred();
}
