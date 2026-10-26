using DefenseGame.Client.Data;
using DefenseGame.Client.Network;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DefenseGame.Client.UI;

public partial class StageSelect : Node2D
{
    [Export] public StageCatalog Catalog { get; set; } = null!;

    public int StageCount => Catalog?.Stages.Count ?? 0;
    public bool IsProgressReady => _progressReady;
    public string ProgressStatusCode => _progressStatusCode;
    public int NextUnlockedStageId => _nextUnlockedStageId;

    private readonly StageProgressServerClient _progressClient = new();
    private readonly CancellationTokenSource _progressCancellation = new();
    private readonly HashSet<int> _clearedStageIds = [];
    private readonly List<Button> _stageButtons = [];
    private VBoxContainer _stageList = null!;
    private Label _stageDescription = null!;
    private Label _selectionStatus = null!;
    private Button _progressRetryButton = null!;
    private int _previewedStageIndex;
    private int _nextUnlockedStageId = 1;
    private bool _progressReady;
    private bool _progressBusy;
    private bool _progressTestOverride;
    private string _progressStatusCode = "Loading";

    public override void _Ready()
    {
        _stageList = GetNode<VBoxContainer>("UI/StagePanel/StageList");
        _stageDescription = GetNode<Label>("UI/MapHint");
        _selectionStatus = GetNode<Label>("UI/SelectionStatus");
        _progressRetryButton = GetNode<Button>("UI/ProgressRetryButton");
        _progressRetryButton.Pressed += () => _ = LoadProgressAsync();
        ApplyOperationStyle();
        BuildStageButtons();
        ShowStageDescription(0);

        if (StageSelectionState.UseLocalProgressionForTests)
            ApplyProgressForTests(StageCount + 1, Enumerable.Range(1, StageCount).ToArray());
        else
            _ = LoadProgressAsync();
    }

    public int GetEnemyCount(int index)
    {
        if (Catalog is null || index < 0 || index >= Catalog.Stages.Count) return 0;
        int total = 0;
        foreach (WaveDefinition wave in Catalog.Stages[index].Waves) total += wave.Count;
        return total;
    }

    public bool IsStageLocked(int index)
    {
        if (!_progressReady || Catalog is null || index < 0 || index >= Catalog.Stages.Count) return true;
        return !StageProgressionId.TryParse(Catalog.Stages[index].Id, out int progressionId)
            || progressionId > _nextUnlockedStageId;
    }

    public string GetStageButtonText(int index) =>
        index >= 0 && index < _stageButtons.Count ? _stageButtons[index].Text : "";

    public void SetSkillProfileStoragePathOverride(string path) =>
        StageSelectionState.SkillProfileStoragePathOverride = path;

    public void SetStageSquadStoragePathOverride(string path)
    {
        StageSelectionState.StageSquadStoragePathOverride = path;
        if (string.IsNullOrWhiteSpace(path)) return;
        StageSelectionState.UseLocalProgressionForTests = true;
        ApplyProgressForTests(StageCount + 1, Enumerable.Range(1, StageCount).ToArray());
    }

    public void ApplyProgressForTests(int nextUnlockedStageId, int[] clearedStageIds)
    {
        _progressTestOverride = true;
        _progressBusy = false;
        _progressReady = true;
        _progressStatusCode = "TestOverride";
        _nextUnlockedStageId = Math.Max(1, nextUnlockedStageId);
        _clearedStageIds.Clear();
        foreach (int stageId in clearedStageIds.Where(id => id > 0)) _clearedStageIds.Add(stageId);
        RefreshStageButtons();
        RefreshProgressStatus();
    }

    public bool SelectStage(int index)
    {
        if (Catalog is null || index < 0 || index >= Catalog.Stages.Count || IsStageLocked(index)) return false;
        StageSelectionState.BeginStage(Catalog.Stages[index]);
        GetTree().ChangeSceneToFile("res://scenes/squad_formation.tscn");
        return true;
    }

    public void ShowStageDescription(int index)
    {
        if (Catalog is null || index < 0 || index >= Catalog.Stages.Count) return;
        StageDefinition stage = Catalog.Stages[index];
        _previewedStageIndex = index;
        _stageDescription.Text = $"{stage.DisplayName}\n\n{UiLocalization.Description(stage.Description)}"
            + $"\n\n{stage.Waves.Count} 웨이브  ·  적 {GetEnemyCount(index)}마리";
        RefreshStageButtons();
        RefreshProgressStatus();
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
    }

    private void RefreshStageButtons()
    {
        if (_stageButtons.Count == 0) return;
        for (int i = 0; i < _stageButtons.Count; i++)
        {
            StageDefinition stage = Catalog.Stages[i];
            bool validId = StageProgressionId.TryParse(stage.Id, out int progressionId);
            bool cleared = validId && _clearedStageIds.Contains(progressionId);
            bool unlocked = _progressReady && validId && progressionId <= _nextUnlockedStageId;
            bool selected = i == _previewedStageIndex;
            string state = !_progressReady ? (_progressBusy ? "조회 중" : "서버 확인 필요")
                : !validId ? "데이터 오류" : cleared ? "클리어" : unlocked ? "도전 가능" : "잠김";
            _stageButtons[i].Text = $"{(selected ? "선택됨 · " : "")}{state}   //   작전 {i + 1:00}\n"
                + $"{stage.DisplayName}     {stage.Waves.Count} 웨이브 · 적 {GetEnemyCount(i)}마리";
            _stageButtons[i].Disabled = !unlocked || _progressBusy;
            OperationUiStyle.ApplyButton(
                _stageButtons[i],
                cleared ? OperationUiTone.Success
                    : unlocked && selected ? OperationUiTone.Primary
                    : unlocked ? OperationUiTone.Neutral : OperationUiTone.Danger,
                selected && unlocked,
                repairRequired: _progressReady && !validId,
                fontSize: 18,
                leftPadding: 22);
        }

        if (_progressReady)
        {
            Button? focusTarget = _stageButtons.FirstOrDefault(button => !button.Disabled);
            if (focusTarget is not null && GetViewport().GuiGetFocusOwner() is null) focusTarget.GrabFocus();
        }
    }

    private void RefreshProgressStatus()
    {
        if (_progressBusy)
        {
            _selectionStatus.Text = "서버 진행도를 확인하고 있습니다...";
            _selectionStatus.Modulate = OperationUiStyle.Amber;
            _progressRetryButton.Visible = false;
            return;
        }
        if (!_progressReady)
        {
            _selectionStatus.Text = $"수정 필요 // 서버 {_progressStatusCode}: 진행도를 불러오지 못했습니다.";
            _selectionStatus.Modulate = OperationUiStyle.Red;
            _progressRetryButton.Visible = true;
            return;
        }

        bool locked = IsStageLocked(_previewedStageIndex);
        bool validId = StageProgressionId.TryParse(Catalog.Stages[_previewedStageIndex].Id, out int progressionId);
        bool cleared = validId && _clearedStageIds.Contains(progressionId);
        _selectionStatus.Text = !validId ? "수정 필요 // 스테이지 진행 ID 형식이 올바르지 않습니다."
            : locked ? "잠김 // 이전 작전을 먼저 클리어하세요."
            : cleared ? "클리어 // 이 작전을 다시 출전할 수 있습니다."
            : "도전 가능 // 버튼을 누르면 편성으로 이동합니다.";
        _selectionStatus.Modulate = !validId || locked ? OperationUiStyle.Red
            : cleared ? OperationUiStyle.Teal : OperationUiStyle.Amber;
        _progressRetryButton.Visible = false;
    }

    private async Task LoadProgressAsync()
    {
        if (_progressBusy || _progressTestOverride) return;
        if (!ClientRuntimeSettings.TryGet(out ClientRuntimeConfiguration configuration, out string error))
        {
            _progressReady = false;
            _progressStatusCode = "invalid_configuration";
            GD.PushError(error);
            RefreshStageButtons();
            RefreshProgressStatus();
            return;
        }
        _progressBusy = true;
        _progressReady = false;
        _progressStatusCode = "Loading";
        RefreshStageButtons();
        RefreshProgressStatus();
        StageProgressLoadResult result = await _progressClient.LoadAsync(
            configuration.ServerBaseUrl, configuration.PlayerId, _progressCancellation.Token);
        if (!IsInsideTree() || _progressTestOverride) return;
        _progressBusy = false;
        if (!result.IsSuccess || result.Payload is null)
        {
            _progressStatusCode = result.ErrorCode;
            _progressReady = false;
            RefreshStageButtons();
            RefreshProgressStatus();
            return;
        }

        _progressStatusCode = "Loaded";
        _progressReady = true;
        _nextUnlockedStageId = Math.Max(1, result.Payload.NextUnlockedStageId);
        _clearedStageIds.Clear();
        foreach (int stageId in result.Payload.ClearedStageIds) _clearedStageIds.Add(stageId);
        RefreshStageButtons();
        RefreshProgressStatus();
    }

    private void ApplyOperationStyle()
    {
        OperationUiStyle.ApplyPanel(GetNode<Panel>("UI/HeaderPanel"), strong: true);
        OperationUiStyle.ApplyPanel(GetNode<Panel>("UI/StagePanel"), strong: true, border: OperationUiStyle.Amber);
        OperationUiStyle.ApplyPanel(GetNode<Panel>("UI/BriefingPanel"), border: OperationUiStyle.Teal);
        OperationUiStyle.ApplyPanel(GetNode<Panel>("UI/RoutePanel"));
        OperationUiStyle.ApplyButton(_progressRetryButton, OperationUiTone.Danger, fontSize: 14);
    }

    public override void _ExitTree()
    {
        _progressCancellation.Cancel();
        _progressCancellation.Dispose();
    }
}
