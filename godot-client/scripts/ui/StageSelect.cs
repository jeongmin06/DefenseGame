using Godot;
using DefenseGame.Client.Data;

namespace DefenseGame.Client.UI;

public partial class StageSelect : Node2D
{
    [Export] public StageCatalog Catalog { get; set; } = null!;
    [Export] public Texture2D StageButtonTexture { get; set; } = null!;

    public int StageCount => Catalog?.Stages.Count ?? 0;

    private VBoxContainer _stageList = null!;
    private Label _stageDescription = null!;

    public override void _Ready()
    {
        _stageList = GetNode<VBoxContainer>("UI/StagePanel/StageList");
        _stageDescription = GetNode<Label>("UI/MapHint");
        BuildStageButtons();
        ShowStageDescription(0);
    }

    public int GetEnemyCount(int index)
    {
        if (Catalog is null || index < 0 || index >= Catalog.Stages.Count) return 0;
        int total = 0;
        foreach (WaveDefinition wave in Catalog.Stages[index].Waves) total += wave.Count;
        return total;
    }

    public void SetSkillProfileStoragePathOverride(string path) =>
        StageSelectionState.SkillProfileStoragePathOverride = path;

    public void SetStageSquadStoragePathOverride(string path) =>
        StageSelectionState.StageSquadStoragePathOverride = path;

    public void SelectStage(int index)
    {
        if (Catalog is null || index < 0 || index >= Catalog.Stages.Count) return;
        StageSelectionState.BeginStage(Catalog.Stages[index]);
        GetTree().ChangeSceneToFile("res://scenes/squad_formation.tscn");
    }

    public void ShowStageDescription(int index)
    {
        if (Catalog is null || index < 0 || index >= Catalog.Stages.Count) return;
        StageDefinition stage = Catalog.Stages[index];
        _stageDescription.Text = $"{stage.DisplayName}\n\n{UiLocalization.Description(stage.Description)}"
            + $"\n\n{stage.Waves.Count} 웨이브  ·  적 {GetEnemyCount(index)}마리";
    }

    private void BuildStageButtons()
    {
        for (int i = 0; i < Catalog.Stages.Count; i++)
        {
            int index = i;
            StageDefinition stage = Catalog.Stages[i];
            var button = new Button
            {
                Name = $"StageButton{i + 1}",
                Text = $"{i + 1:00}   {stage.DisplayName}\n       {stage.Waves.Count} 웨이브   //   적 {GetEnemyCount(i)}마리",
                CustomMinimumSize = new Vector2(560, 96),
                Alignment = HorizontalAlignment.Left,
                TooltipText = UiLocalization.Description(stage.Description)
            };
            button.AddThemeFontSizeOverride("font_size", 20);
            button.AddThemeColorOverride("font_color", new Color("fff0c2"));
            button.AddThemeColorOverride("font_hover_color", Colors.White);
            button.AddThemeStyleboxOverride("normal", CardStyle(new Color("e5e0d8")));
            button.AddThemeStyleboxOverride("hover", CardStyle(new Color("fff0c2")));
            button.AddThemeStyleboxOverride("focus", CardStyle(new Color("ffd166")));
            button.Pressed += () => SelectStage(index);
            button.MouseEntered += () => ShowStageDescription(index);
            button.FocusEntered += () => ShowStageDescription(index);
            _stageList.AddChild(button);
        }

        if (_stageList.GetChildCount() > 0)
            _stageList.GetChild<Control>(0).GrabFocus();
    }

    private StyleBoxTexture CardStyle(Color modulate)
    {
        return new StyleBoxTexture
        {
            Texture = StageButtonTexture,
            ModulateColor = modulate,
            ContentMarginLeft = 34,
            ContentMarginRight = 24,
            ContentMarginTop = 10,
            ContentMarginBottom = 10
        };
    }
}
