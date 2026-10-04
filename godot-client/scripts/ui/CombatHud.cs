using Godot;

namespace DefenseGame.Client.UI;

public partial class CombatHud : CanvasLayer
{
    private Label _placementLabel = null!;
    private Label _waveLabel = null!;
    private Label _enemyLabel = null!;
    private Label _scoreLabel = null!;
    private Label _baseLabel = null!;
    private Panel _resultPanel = null!;
    private Label _resultTitle = null!;
    private Label _resultDetail = null!;
    private Button _restartButton = null!;

    public override void _Ready()
    {
        _placementLabel = GetNode<Label>("TopPanel/PlacementLabel");
        _waveLabel = GetNode<Label>("TopPanel/WaveLabel");
        _enemyLabel = GetNode<Label>("TopPanel/EnemyLabel");
        _scoreLabel = GetNode<Label>("TopPanel/ScoreLabel");
        _baseLabel = GetNode<Label>("TopPanel/BaseLabel");
        _resultPanel = GetNode<Panel>("ResultPanel");
        _resultTitle = GetNode<Label>("ResultPanel/ResultTitle");
        _resultDetail = GetNode<Label>("ResultPanel/ResultDetail");
        _restartButton = GetNode<Button>("ResultPanel/RestartButton");
        _restartButton.Pressed += RestartStage;
    }

    public void UpdatePlacement(int remainingArchers)
    {
        _placementLabel.Text = remainingArchers > 0
            ? $"ARCHERS LEFT  {remainingArchers}  //  SELECT A GROUND TILE"
            : "ARCHERS LEFT  0  //  BATTLE START";
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
        _resultPanel.Visible = true;
        _resultTitle.Text = victory ? "STAGE CLEAR" : "GATE LOST";
        _resultTitle.Modulate = victory ? Rgb(255, 209, 102) : Rgb(255, 107, 94);
        _resultDetail.Text = $"Defeated {defeated}  /  Escaped {escaped}";
        _restartButton.GrabFocus();
    }

    private void RestartStage()
    {
        GetTree().ReloadCurrentScene();
    }

    private static Color Rgb(byte red, byte green, byte blue, byte alpha = 255)
    {
        return new Color(red / 255.0f, green / 255.0f, blue / 255.0f, alpha / 255.0f);
    }
}
