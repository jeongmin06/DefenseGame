using Godot;
using DefenseGame.Client.Data;

namespace DefenseGame.Client.UI;

// Root-owned overlay survives scene replacement. No combat scene exists until loading finishes.
public partial class LoadingScreen : CanvasLayer
{
    private const string BattlePath = "res://scenes/stage_one.tscn";
    private static bool _bootShown;
    private static LoadingScreen? _active;
    [Export] public string TargetScene { get; set; } = BattlePath;
    private bool _enterBattle;
    private bool _failed;
    private double _elapsed;
    private Label _status = null!;
    private ProgressBar _progress = null!;
    private Button _retry = null!;
    private Button _back = null!;
    private Node? _previousScene;
    private Node.ProcessModeEnum _previousMode;

    public static void ShowBoot(Node owner)
    {
        if (_bootShown) return;
        _bootShown = true;
        Show(owner, false);
    }

    public static void EnterBattle(Node owner) => Show(owner, true);

    private static void Show(Node owner, bool enterBattle)
    {
        // Tests and very fast navigation may request battle while boot preloading is active.
        if (IsInstanceValid(_active)) _active!.Close();
        var screen = GD.Load<PackedScene>("res://scenes/loading_screen.tscn").Instantiate<LoadingScreen>();
        screen._enterBattle = enterBattle;
        screen._previousScene = owner.GetTree().CurrentScene;
        if (screen._previousScene is not null)
        {
            screen._previousMode = screen._previousScene.ProcessMode;
            screen._previousScene.ProcessMode = Node.ProcessModeEnum.Disabled;
        }
        _active = screen;
        owner.GetTree().Root.AddChild(screen);
    }

    public override void _Ready()
    {
        Layer = 100;
        _status = GetNode<Label>("UI/Status");
        _progress = GetNode<ProgressBar>("UI/Progress");
        _retry = GetNode<Button>("UI/Retry");
        _back = GetNode<Button>("UI/Back");
        GetNode<Label>("UI/Title").Text = _enterBattle
            ? StageSelectionState.SelectedStage?.DisplayName ?? "초원 방어선"
            : "초원으로, 함께";
        _retry.Pressed += BeginLoading;
        _back.Pressed += () =>
        {
            Close();
            GetTree().ChangeSceneToFile("res://scenes/stage_select.tscn");
        };
        BeginLoading();
    }

    private void BeginLoading()
    {
        _failed = false;
        _elapsed = 0;
        _progress.Value = 0;
        _retry.Hide();
        _back.Hide();
        _status.Text = "방어선 준비 중…";
        if (!ResourceLoader.Exists(TargetScene) || ResourceLoader.LoadThreadedRequest(TargetScene, "PackedScene") != Error.Ok)
            Fail();
    }

    public override void _Process(double delta)
    {
        if (_failed) return;
        _elapsed += delta;
        var progress = new Godot.Collections.Array();
        var status = ResourceLoader.LoadThreadedGetStatus(TargetScene, progress);
        if (progress.Count > 0) _progress.Value = progress[0].AsDouble() * 100;
        if (status is ResourceLoader.ThreadLoadStatus.Failed or ResourceLoader.ThreadLoadStatus.InvalidResource)
        {
            Fail();
            return;
        }
        if (status != ResourceLoader.ThreadLoadStatus.Loaded) return;
        _progress.Value = 100;
        _status.Text = "준비 완료";
        // Brief readable presentation; headless automation does not need a visual hold.
        if (DisplayServer.GetName() != "headless" && _elapsed < 1.1) return;
        var scene = ResourceLoader.LoadThreadedGet(TargetScene) as PackedScene;
        if (scene is null) { Fail(); return; }
        if (_enterBattle)
        {
            var result = GetTree().ChangeSceneToPacked(scene);
            if (result != Error.Ok) { Fail(); return; }
        }
        Close();
    }

    private void Fail()
    {
        _failed = true;
        _status.Text = "방어선을 불러오지 못했습니다. 다시 시도해 주세요.";
        _retry.Show();
        _back.Show();
        _retry.GrabFocus();
    }

    private void Close()
    {
        if (IsInstanceValid(_previousScene)) _previousScene!.ProcessMode = _previousMode;
        if (_active == this) _active = null;
        SetProcess(false);
        QueueFree();
    }
}
