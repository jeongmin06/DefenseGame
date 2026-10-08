using Godot;
using DefenseGame.Client.Combat;

namespace DefenseGame.Client.UI;

public partial class CombatHud : CanvasLayer
{
    [Signal] public delegate void ArcherSelectedEventHandler();
    [Signal] public delegate void WarriorSelectedEventHandler();
    [Signal] public delegate void HealerSelectedEventHandler();
    [Signal] public delegate void RetryRequestedEventHandler();
    [Signal] public delegate void StageListRequestedEventHandler();
    private Button _healerButton = null!;
    private Button _archerButton = null!;
    private Button _warriorButton = null!;
    private Label _placementLabel = null!;
    private Label _waveLabel = null!;
    private Label _enemyLabel = null!;
    private Label _scoreLabel = null!;
    private Label _baseLabel = null!;
    private Panel _resultPanel = null!;
    private Label _resultTitle = null!;
    private Label _resultDetail = null!;
    private Button _restartButton = null!;
    private Button _stageListButton = null!;
    private Label _title = null!;

    public override void _Ready()
    {
        _healerButton = GetNode<Button>("TopPanel/HealerButton");
        _title = GetNode<Label>("TopPanel/Title");
        _healerButton.Pressed += () => EmitSignal(SignalName.HealerSelected);
        _archerButton = GetNode<Button>("TopPanel/ArcherButton");
        _warriorButton = GetNode<Button>("TopPanel/WarriorButton");
        _archerButton.Pressed += () => EmitSignal(SignalName.ArcherSelected);
        _warriorButton.Pressed += () => EmitSignal(SignalName.WarriorSelected);
        _placementLabel = GetNode<Label>("TopPanel/PlacementLabel");
        _waveLabel = GetNode<Label>("TopPanel/WaveLabel");
        _enemyLabel = GetNode<Label>("TopPanel/EnemyLabel");
        _scoreLabel = GetNode<Label>("TopPanel/ScoreLabel");
        _baseLabel = GetNode<Label>("TopPanel/BaseLabel");
        _resultPanel = GetNode<Panel>("ResultPanel");
        _resultTitle = GetNode<Label>("ResultPanel/ResultTitle");
        _resultDetail = GetNode<Label>("ResultPanel/ResultDetail");
        _restartButton = GetNode<Button>("ResultPanel/RestartButton");
        _restartButton.Pressed += () => EmitSignal(SignalName.RetryRequested);
        _stageListButton = GetNode<Button>("ResultPanel/StageListButton");
        _stageListButton.Pressed += () => EmitSignal(SignalName.StageListRequested);
    }

    public void UpdateStage(string displayName, string id)
    {
        _title.Text = $"{displayName.ToUpperInvariant()}  //  {id.Replace('_', ' ').ToUpperInvariant()}";
    }

    public void UpdatePlacement(int archers, int warriors, int healers, DeploymentGrid.PlacementType selected,
        string nextArcher = "")
    {
        _healerButton.Text = $"HEALER {healers}";
        _healerButton.Disabled = healers == 0;
        _archerButton.Text = $"ARCHER {archers}";
        _warriorButton.Text = $"WARRIOR {warriors}";
        _archerButton.Disabled = archers == 0;
        _warriorButton.Disabled = warriors == 0;
        string name = selected switch
        {
            DeploymentGrid.PlacementType.Melee => "WARRIOR",
            DeploymentGrid.PlacementType.Support => "HEALER",
            _ => "ARCHER"
        };
        _placementLabel.Text = archers + warriors + healers == 0 ? "BATTLE START"
            : selected == DeploymentGrid.PlacementType.Melee ? $"{name} // GROUND OR PATH"
            : selected == DeploymentGrid.PlacementType.Ranged && !string.IsNullOrEmpty(nextArcher)
                ? $"{name} // {nextArcher} // SELECT GROUND"
                : $"{name} // SELECT GROUND";
    }

    public void UpdateStatus(
        int waveNumber,
        int totalWaves,
        int alive,
        int defeated,
        int escaped,
        int baseHealth)
    {
        _waveLabel.Text = $"WAVE {waveNumber:D2} / {totalWaves:D2}";
        _enemyLabel.Text = $"ENEMIES  {alive:D2}";
        _scoreLabel.Text = $"DEFEATED  {defeated:D2}   ESCAPED  {escaped:D2}";
        _baseLabel.Text = $"GATE  {baseHealth:D2}";
    }

    public void ShowResult(bool victory, int defeated, int escaped)
    {
        _placementLabel.Text = "BATTLE ENDED";
        _archerButton.Disabled = true;
        _warriorButton.Disabled = true;
        _healerButton.Disabled = true;
        _resultPanel.Visible = true;
        _resultTitle.Text = victory ? "STAGE CLEAR" : "GATE LOST";
        _resultTitle.Modulate = victory ? Rgb(255, 209, 102) : Rgb(255, 107, 94);
        _resultDetail.Text = $"Defeated {defeated}  /  Escaped {escaped}";
        _restartButton.GrabFocus();
    }

    private static Color Rgb(byte red, byte green, byte blue, byte alpha = 255)
    {
        return new Color(red / 255.0f, green / 255.0f, blue / 255.0f, alpha / 255.0f);
    }
}
