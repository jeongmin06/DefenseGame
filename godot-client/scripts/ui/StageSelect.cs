using Godot;
using DefenseGame.Client.Data;

namespace DefenseGame.Client.UI;

public partial class StageSelect : Node2D
{
    [Export] public StageCatalog Catalog { get; set; } = null!;

    public int StageCount => Catalog?.Stages.Count ?? 0;

    private VBoxContainer _stageList = null!;

    public override void _Ready()
    {
        _stageList = GetNode<VBoxContainer>("UI/StagePanel/StageList");
        BuildStageButtons();
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(0, 0, 1280, 720), new Color("183b2b"));
        DrawCircle(new Vector2(1110, 105), 180, new Color("d8953d"));
        DrawCircle(new Vector2(1110, 105), 138, new Color("efba58"));
        var route = new Vector2[]
        {
            new(-80, 610), new(250, 520), new(455, 625), new(760, 520), new(1090, 610), new(1360, 470)
        };
        DrawPolyline(route, new Color("8b5831"), 88, true);
        DrawPolyline(route, new Color("c58b4b"), 62, true);
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

    private void BuildStageButtons()
    {
        for (int i = 0; i < Catalog.Stages.Count; i++)
        {
            int index = i;
            StageDefinition stage = Catalog.Stages[i];
            var button = new Button
            {
                Name = $"StageButton{i + 1}",
                Text = $"{i + 1:00}   {stage.DisplayName.ToUpperInvariant()}\n       {stage.Waves.Count} WAVES   //   {GetEnemyCount(i)} ENEMIES",
                CustomMinimumSize = new Vector2(560, 84),
                Alignment = HorizontalAlignment.Left
            };
            button.AddThemeFontSizeOverride("font_size", 20);
            button.AddThemeColorOverride("font_color", new Color("fff0c2"));
            button.AddThemeColorOverride("font_hover_color", Colors.White);
            button.AddThemeStyleboxOverride("normal", CardStyle("6f3424", "d8943f"));
            button.AddThemeStyleboxOverride("hover", CardStyle("8d4429", "ffd166"));
            button.AddThemeStyleboxOverride("focus", CardStyle("8d4429", "fff0c2"));
            button.Pressed += () => SelectStage(index);
            _stageList.AddChild(button);
        }

        if (_stageList.GetChildCount() > 0)
            _stageList.GetChild<Control>(0).GrabFocus();
    }

    private static StyleBoxFlat CardStyle(string background, string border)
    {
        return new StyleBoxFlat
        {
            BgColor = new Color(background),
            BorderColor = new Color(border),
            BorderWidthLeft = 3,
            BorderWidthTop = 3,
            BorderWidthRight = 3,
            BorderWidthBottom = 3,
            CornerRadiusTopLeft = 8,
            CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8,
            CornerRadiusBottomRight = 8,
            ContentMarginLeft = 22
        };
    }
}
