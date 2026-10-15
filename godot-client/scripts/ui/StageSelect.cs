using Godot;
using DefenseGame.Client.Data;
using System.Collections.Generic;

namespace DefenseGame.Client.UI;

public partial class StageSelect : Node2D
{
    [Export] public StageCatalog Catalog { get; set; } = null!;

    public int StageCount => Catalog?.Stages.Count ?? 0;

    private readonly List<Button> _stageButtons = [];
    private VBoxContainer _stageList = null!;
    private Label _stageDescription = null!;
    private int _previewedStageIndex;

    public override void _Ready()
    {
        _stageList = GetNode<VBoxContainer>("UI/StagePanel/StageList");
        _stageDescription = GetNode<Label>("UI/MapHint");
        ApplyOperationStyle();
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
        _previewedStageIndex = index;
        _stageDescription.Text = $"{stage.DisplayName}\n\n{UiLocalization.Description(stage.Description)}"
            + $"\n\n{stage.Waves.Count} 웨이브  ·  적 {GetEnemyCount(index)}마리";
        RefreshStageButtons();
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
                CustomMinimumSize = new Vector2(608, 112),
                Alignment = HorizontalAlignment.Left,
                TooltipText = UiLocalization.Description(stage.Description)
            };
            button.Pressed += () => SelectStage(index);
            button.MouseEntered += () => ShowStageDescription(index);
            button.FocusEntered += () => ShowStageDescription(index);
            _stageButtons.Add(button);
            _stageList.AddChild(button);
        }

        if (_stageList.GetChildCount() > 0)
            _stageList.GetChild<Control>(0).GrabFocus();
    }

    private void RefreshStageButtons()
    {
        for (int i = 0; i < _stageButtons.Count; i++)
        {
            StageDefinition stage = Catalog.Stages[i];
            bool selected = i == _previewedStageIndex;
            _stageButtons[i].Text = $"{(selected ? "선택됨" : "대기")}   //   작전 {i + 1:00}\n"
                + $"{stage.DisplayName}     {stage.Waves.Count} 웨이브 · 적 {GetEnemyCount(i)}마리";
            OperationUiStyle.ApplyButton(
                _stageButtons[i],
                selected ? OperationUiTone.Primary : OperationUiTone.Neutral,
                selected,
                fontSize: 18,
                leftPadding: 22);
        }
    }

    private void ApplyOperationStyle()
    {
        OperationUiStyle.ApplyPanel(GetNode<Panel>("UI/HeaderPanel"), strong: true);
        OperationUiStyle.ApplyPanel(GetNode<Panel>("UI/StagePanel"), strong: true, border: OperationUiStyle.Amber);
        OperationUiStyle.ApplyPanel(GetNode<Panel>("UI/BriefingPanel"), border: OperationUiStyle.Teal);
        OperationUiStyle.ApplyPanel(GetNode<Panel>("UI/RoutePanel"));
    }
}
