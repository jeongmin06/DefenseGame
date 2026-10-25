using Godot;
using DefenseGame.Client.Combat;
using DefenseGame.Client.Data;
using DefenseGame.Client.Visuals;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DefenseGame.Client.UI;

public partial class CombatHud : CanvasLayer
{
    [Signal] public delegate void CharacterSelectedEventHandler(string characterId);
    [Signal] public delegate void CharacterDroppedEventHandler(string characterId, Vector2 globalPosition);
    [Signal] public delegate void CharacterDragMovedEventHandler(string characterId, Vector2 globalPosition);
    [Signal] public delegate void CharacterDragEndedEventHandler();
    [Signal] public delegate void RetreatRequestedEventHandler(string characterId);
    [Signal] public delegate void PauseRequestedEventHandler();
    [Signal] public delegate void BattleSpeedRequestedEventHandler();
    [Signal] public delegate void FirstWaveStartRequestedEventHandler();
    [Signal] public delegate void RetryRequestedEventHandler();
    [Signal] public delegate void StageListRequestedEventHandler();
    [Signal] public delegate void ResultSyncRetryRequestedEventHandler();
    private readonly Dictionary<string, Button> _deploymentCards = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CatProfile> _profiles = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _cardVisualStates = new(StringComparer.Ordinal);
    private HBoxContainer _deploymentCardList = null!;
    private Label _placementLabel = null!;
    private Label _deploymentPointsLabel = null!;
    private Label _waveLabel = null!;
    private Label _enemyLabel = null!;
    private Label _scoreLabel = null!;
    private Label _baseLabel = null!;
    private Panel _resultPanel = null!;
    private Label _resultTitle = null!;
    private Label _resultDetail = null!;
    private Label _resultSyncLabel = null!;
    private Button _resultSyncRetryButton = null!;
    private Button _restartButton = null!;
    private Button _stageListButton = null!;
    private Label _title = null!;
    private PanelContainer _dragPreview = null!;
    private Label _dragPreviewLabel = null!;
    private DirectionalAnimatedSprite _dragGhost = null!;
    private PanelContainer _unitSelectionPanel = null!;
    private Label _unitSelectionLabel = null!;
    private Button _retreatButton = null!;
    private Button _pauseButton = null!;
    private Button _speedButton = null!;
    private Button _firstWaveButton = null!;
    private Vector2 _dragGhostOffset;
    private string _dragPreviewSummary = "";
    private string _dragCandidateId = "";
    private string _draggedCharacterId = "";
    private Vector2 _dragStart;
    private string _selectedUnitCharacterId = "";

    public int DeploymentCardCount => _deploymentCards.Count;
    public string[] DeploymentCardCharacterIds => _deploymentCards.Keys.ToArray();
    public bool IsDraggingCharacter => !string.IsNullOrEmpty(_draggedCharacterId);
    public string DraggedCharacterId => _draggedCharacterId;
    public bool DragPlacementValid { get; private set; }
    public string DragPlacementStatus { get; private set; } = "";
    public string ResultSyncStatus { get; private set; } = "Idle";
    public bool IsUnitSelectionVisible => _unitSelectionPanel?.Visible ?? false;
    public string SelectedUnitCharacterId => _selectedUnitCharacterId;
    public string SelectedUnitSummary => _unitSelectionLabel?.Text ?? "";
    public string RetreatButtonText => _retreatButton?.Text ?? "";
    public string PauseButtonText => _pauseButton?.Text ?? "";
    public string SpeedButtonText => _speedButton?.Text ?? "";
    public bool IsFirstWaveButtonDisabled => _firstWaveButton?.Disabled ?? true;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        _title = GetNode<Label>("TopPanel/Title");
        _deploymentCardList = GetNode<HBoxContainer>("DeploymentPanel/CardScroll/CardList");
        _placementLabel = GetNode<Label>("TopPanel/PlacementLabel");
        _deploymentPointsLabel = GetNode<Label>("DeploymentPanel/DeploymentPointsLabel");
        _waveLabel = GetNode<Label>("TopPanel/WaveLabel");
        _enemyLabel = GetNode<Label>("TopPanel/EnemyLabel");
        _scoreLabel = GetNode<Label>("TopPanel/ScoreLabel");
        _baseLabel = GetNode<Label>("TopPanel/BaseLabel");
        _resultPanel = GetNode<Panel>("ResultPanel");
        _resultTitle = GetNode<Label>("ResultPanel/ResultTitle");
        _resultDetail = GetNode<Label>("ResultPanel/ResultDetail");
        _resultSyncLabel = GetNode<Label>("ResultPanel/ResultSyncLabel");
        _resultSyncRetryButton = GetNode<Button>("ResultPanel/ResultSyncRetryButton");
        _resultSyncRetryButton.Pressed += () => EmitSignal(SignalName.ResultSyncRetryRequested);
        _restartButton = GetNode<Button>("ResultPanel/RestartButton");
        _restartButton.Pressed += () => EmitSignal(SignalName.RetryRequested);
        _stageListButton = GetNode<Button>("ResultPanel/StageListButton");
        _stageListButton.Pressed += () => EmitSignal(SignalName.StageListRequested);
        ApplyOperationStyle();
        CreateDragPreview();
        CreateUnitSelectionPanel();
        CreateBattleControls();
    }

    public override void _Input(InputEvent input)
    {
        if (input is InputEventMouseMotion motion && !string.IsNullOrEmpty(_dragCandidateId))
        {
            if (!IsDraggingCharacter && motion.GlobalPosition.DistanceTo(_dragStart) >= 12.0f)
                BeginCharacterDrag(_dragCandidateId, motion.GlobalPosition);
            if (IsDraggingCharacter)
            {
                MoveDragPreview(motion.GlobalPosition);
                EmitSignal(SignalName.CharacterDragMoved, _draggedCharacterId, motion.GlobalPosition);
            }
        }
        else if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } released)
        {
            if (CompleteCharacterDrag(released.GlobalPosition)) GetViewport().SetInputAsHandled();
            else CancelCharacterDrag();
        }
        else if (input is InputEventKey { Keycode: Key.Escape, Pressed: true } && IsDraggingCharacter)
        {
            CancelCharacterDrag();
            GetViewport().SetInputAsHandled();
        }
    }

    public void UpdateStage(string displayName, string id)
    {
        _title.Text = displayName;
        _title.TooltipText = $"관리 ID: {id}";
    }

    public void ConfigureDeploymentCards(IEnumerable<CatProfile> profiles)
    {
        foreach (Node child in _deploymentCardList.GetChildren()) child.QueueFree();
        _deploymentCards.Clear();
        _profiles.Clear();
        _cardVisualStates.Clear();
        foreach (CatProfile profile in profiles)
        {
            if (profile.Unit is null) continue;
            string characterId = profile.CharacterId;
            var button = new Button
            {
                Name = $"Card_{characterId}",
                CustomMinimumSize = new Vector2(218, 70),
                Alignment = HorizontalAlignment.Left
            };
            OperationUiStyle.ApplyButton(button, fontSize: 14, leftPadding: 12);
            button.TooltipText = $"{UiLocalization.Description(profile.Description)}\n관리 ID: {characterId}";
            button.ButtonDown += () => PrepareCharacterDrag(characterId);
            button.Pressed += () => EmitSignal(SignalName.CharacterSelected, characterId);
            _profiles.Add(characterId, profile);
            _deploymentCards.Add(characterId, button);
            _deploymentCardList.AddChild(button);
        }
    }

    public void UpdateDeploymentCards(
        IEnumerable<string> remainingCharacterIds,
        string selectedCharacterId,
        float deploymentPoints,
        IReadOnlyDictionary<string, float>? redeployRemaining = null,
        bool interactionsLocked = false)
    {
        var remaining = new HashSet<string>(remainingCharacterIds, StringComparer.Ordinal);
        foreach ((string characterId, Button button) in _deploymentCards)
        {
            CatProfile profile = _profiles[characterId];
            UnitDefinition unit = profile.Unit!;
            bool available = remaining.Contains(characterId);
            float remainingSeconds = 0;
            bool waiting = redeployRemaining is not null
                && redeployRemaining.TryGetValue(characterId, out remainingSeconds)
                && remainingSeconds > 0;
            bool affordable = available && deploymentPoints >= unit.DeploymentCost;
            bool selected = affordable && characterId == selectedCharacterId;
            string visualState = waiting ? "cooldown" : !available ? "placed"
                : selected ? "selected" : affordable ? "ready" : "insufficient";
            if (!_cardVisualStates.TryGetValue(characterId, out string? previousState) || previousState != visualState)
            {
                OperationUiStyle.ApplyButton(
                    button,
                    visualState switch
                    {
                        "placed" => OperationUiTone.Success,
                        "cooldown" => OperationUiTone.Neutral,
                        "selected" => OperationUiTone.Primary,
                        "insufficient" => OperationUiTone.Danger,
                        _ => OperationUiTone.Neutral
                    },
                    selected,
                    fontSize: 14,
                    leftPadding: 12);
                _cardVisualStates[characterId] = visualState;
            }
            button.Disabled = interactionsLocked || !affordable;
            button.Text = waiting
                ? $"↻ {profile.DisplayName}\n  {KoreanUiText.Role(unit.Role)} · 재배치 {Mathf.CeilToInt(remainingSeconds):D2}초"
                : available
                ? $"{(selected ? "▶ " : "  ")}{profile.DisplayName}\n  {KoreanUiText.Role(unit.Role)} · 비용 {unit.DeploymentCost}{(affordable ? " · 배치 가능" : " · 포인트 부족")}"
                : $"✓ {profile.DisplayName}\n  {KoreanUiText.Role(unit.Role)} · 전장 배치 중";
            button.Modulate = selected ? Colors.White : affordable ? new Color("dce7ea")
                : available ? new Color("d99a94") : waiting ? new Color("9babb0") : new Color("80bdb4");
            if (!available && (_dragCandidateId == characterId || _draggedCharacterId == characterId))
                CancelCharacterDrag();
        }
    }

    public void UpdateDeploymentPoints(int current, int maximum)
    {
        _deploymentPointsLabel.Text = $"배치 포인트 {current} / {maximum}";
    }

    public void UpdatePlacement(
        CatProfile? selectedProfile,
        int remainingCount,
        bool choosingFacing = false,
        bool hasRedeployCooldown = false,
        bool hasAffordableCards = false,
        bool battlePaused = false)
    {
        if (battlePaused)
        {
            _placementLabel.Text = "전투 일시정지 // 계속을 눌러 재개";
            return;
        }
        if (choosingFacing)
        {
            _placementLabel.Text = "배치 방향 선택 // 범위를 확인하고 다시 클릭 · ESC 취소";
            return;
        }
        if (remainingCount == 0)
        {
            _placementLabel.Text = hasRedeployCooldown ? "고양이 재배치 대기 중" : "모든 고양이 배치 완료";
            return;
        }
        if (selectedProfile?.Unit is null)
        {
            _placementLabel.Text = hasAffordableCards ? "배치할 고양이를 선택하세요" : "배치 포인트 회복 대기";
            return;
        }
        string terrain = selectedProfile.Unit.Role == UnitRole.Melee ? "잔디 또는 경로" : "잔디";
        _placementLabel.Text = $"{selectedProfile.DisplayName} 선택 // {terrain} 타일 지정";
    }

    public void UpdateStatus(
        int waveNumber,
        int totalWaves,
        int alive,
        int defeated,
        int escaped,
        int baseHealth,
        int secondsUntilFirstWave = -1)
    {
        _waveLabel.Text = waveNumber == 0 && secondsUntilFirstWave >= 0
            ? $"첫 웨이브 {secondsUntilFirstWave:D2}초"
            : $"웨이브 {waveNumber:D2} / {totalWaves:D2}";
        _enemyLabel.Text = $"전장 적  {alive:D2}";
        _scoreLabel.Text = $"처치  {defeated:D2}   탈출  {escaped:D2}";
        _baseLabel.Text = $"기지  {baseHealth:D2}";
    }

    public void ShowResult(bool victory, int defeated, int escaped)
    {
        CancelCharacterDrag();
        HideUnitSelection();
        _placementLabel.Text = "전투 종료";
        foreach (Button button in _deploymentCards.Values) button.Disabled = true;
        UpdateBattleControls(paused: false, speed: 1, canStartFirstWave: false, battleEnded: true);
        _resultPanel.Visible = true;
        _resultTitle.Text = victory ? "작전 성공" : "기지 함락";
        _resultTitle.Modulate = victory ? OperationUiStyle.Teal : OperationUiStyle.Red;
        OperationUiStyle.ApplyPanel(
            _resultPanel,
            strong: true,
            border: victory ? OperationUiStyle.Teal : OperationUiStyle.Red);
        _resultDetail.Text = $"처치 {defeated}  /  탈출 {escaped}";
        ShowResultSyncPending();
    }

    public void ShowResultSyncPending()
    {
        ResultSyncStatus = "Saving";
        _resultSyncLabel.Text = "서버에 전투 결과를 저장하고 있습니다...";
        _resultSyncLabel.Modulate = OperationUiStyle.Amber;
        _resultSyncRetryButton.Visible = false;
        _restartButton.Disabled = true;
        _stageListButton.Disabled = true;
    }

    public void ShowResultSyncSucceeded(string message)
    {
        ResultSyncStatus = "Saved";
        _resultSyncLabel.Text = message;
        _resultSyncLabel.Modulate = OperationUiStyle.Teal;
        _resultSyncRetryButton.Visible = false;
        _restartButton.Disabled = false;
        _stageListButton.Disabled = false;
        _restartButton.GrabFocus();
    }

    public void ShowResultSyncFailed(string errorCode)
    {
        ResultSyncStatus = errorCode;
        _resultSyncLabel.Text = $"저장 실패 // {errorCode}\n서버 연결을 확인하고 다시 시도하세요.";
        _resultSyncLabel.Modulate = OperationUiStyle.Red;
        _resultSyncRetryButton.Visible = true;
        _resultSyncRetryButton.Disabled = false;
        _restartButton.Disabled = true;
        _stageListButton.Disabled = true;
        _resultSyncRetryButton.GrabFocus();
    }

    public bool IsDeploymentCardDisabled(string characterId) =>
        _deploymentCards.TryGetValue(characterId, out Button? button) && button.Disabled;

    public string GetDeploymentCardText(string characterId) =>
        _deploymentCards.TryGetValue(characterId, out Button? button) ? button.Text : "";

    public bool AreAllDeploymentCardsDisabled() => _deploymentCards.Values.All(button => button.Disabled);

    public void UpdateBattleControls(bool paused, int speed, bool canStartFirstWave, bool battleEnded = false)
    {
        _pauseButton.Text = paused ? "계속" : "일시정지";
        _pauseButton.Disabled = battleEnded;
        _speedButton.Text = $"{speed}×";
        _speedButton.Disabled = battleEnded;
        _firstWaveButton.Disabled = battleEnded || paused || !canStartFirstWave;
        _firstWaveButton.Text = canStartFirstWave ? "웨이브 시작" : "진행 중";
        if (_retreatButton is not null) _retreatButton.Disabled = battleEnded || paused;
    }

    public void ShowUnitSelection(
        string characterId,
        string displayName,
        UnitRole role,
        float currentHealth,
        float maxHealth,
        int refund)
    {
        _selectedUnitCharacterId = characterId;
        _unitSelectionLabel.Text = $"{displayName}  //  {KoreanUiText.Role(role)}\n체력 {Mathf.CeilToInt(currentHealth)} / {Mathf.CeilToInt(maxHealth)}";
        _retreatButton.Text = $"퇴각  //  배치 포인트 +{refund}";
        _placementLabel.Text = $"{displayName} 선택 // 전장 유지 또는 퇴각 결정";
        _retreatButton.Disabled = false;
        _unitSelectionPanel.Visible = true;
    }

    public void HideUnitSelection()
    {
        _selectedUnitCharacterId = "";
        if (_unitSelectionPanel is not null) _unitSelectionPanel.Visible = false;
    }

    public bool BeginCharacterDrag(string characterId, Vector2 globalPosition)
    {
        if (!_deploymentCards.TryGetValue(characterId, out Button? button) || button.Disabled
            || !_profiles.TryGetValue(characterId, out CatProfile? profile) || profile.Unit is null)
            return false;
        _dragCandidateId = characterId;
        _draggedCharacterId = characterId;
        _dragPreviewSummary = $"{profile.DisplayName}\n{KoreanUiText.Role(profile.Unit.Role)} · 비용 {profile.Unit.DeploymentCost}";
        DragPlacementValid = false;
        DragPlacementStatus = "타일 확인 중";
        _dragPreviewLabel.Text = $"{_dragPreviewSummary}\n{DragPlacementStatus}";
        ConfigureDragGhost(profile.Unit.Visual);
        _dragPreview.Visible = true;
        _dragGhost.Visible = true;
        EmitSignal(SignalName.CharacterSelected, characterId);
        MoveDragPreview(globalPosition);
        EmitSignal(SignalName.CharacterDragMoved, characterId, globalPosition);
        return true;
    }

    public void UpdateCharacterDragPreview(Vector2 snappedGlobalPosition, bool valid, string status)
    {
        if (!IsDraggingCharacter) return;
        bool stateChanged = DragPlacementValid != valid || DragPlacementStatus != status;
        DragPlacementValid = valid;
        DragPlacementStatus = status;
        _dragGhost.Position = snappedGlobalPosition + _dragGhostOffset;
        if (!stateChanged) return;
        _dragPreviewLabel.Text = $"{_dragPreviewSummary}\n{status}";
        Color stateColor = valid ? OperationUiStyle.Teal : OperationUiStyle.Red;
        _dragPreview.AddThemeStyleboxOverride(
            "panel",
            OperationUiStyle.PanelStyle(strong: true, border: stateColor));
        ApplyDragGhostTint(stateColor);
    }

    public void CancelCharacterDrag()
    {
        bool wasDragging = IsDraggingCharacter;
        _dragCandidateId = "";
        _draggedCharacterId = "";
        DragPlacementValid = false;
        DragPlacementStatus = "";
        if (_dragPreview is not null) _dragPreview.Visible = false;
        if (_dragGhost is not null) _dragGhost.Visible = false;
        if (wasDragging) EmitSignal(SignalName.CharacterDragEnded);
    }

    public bool CompleteCharacterDrag(Vector2 globalPosition)
    {
        if (!IsDraggingCharacter) return false;
        string characterId = _draggedCharacterId;
        EmitSignal(SignalName.CharacterDropped, characterId, globalPosition);
        CancelCharacterDrag();
        return true;
    }

    private void PrepareCharacterDrag(string characterId)
    {
        _dragCandidateId = characterId;
        _dragStart = GetViewport().GetMousePosition();
    }

    private void MoveDragPreview(Vector2 globalPosition)
    {
        _dragPreview.Position = globalPosition + new Vector2(22, -82);
        if (_dragGhost.Visible) _dragGhost.Position = globalPosition + _dragGhostOffset;
    }

    private void CreateDragPreview()
    {
        _dragPreview = new PanelContainer
        {
            Name = "DragPreview",
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ZIndex = 101,
            CustomMinimumSize = new Vector2(196, 68)
        };
        _dragPreview.AddThemeStyleboxOverride("panel", OperationUiStyle.PanelStyle(strong: true, border: OperationUiStyle.Amber));
        _dragPreviewLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _dragPreviewLabel.AddThemeFontSizeOverride("font_size", 15);
        _dragPreviewLabel.AddThemeColorOverride("font_color", OperationUiStyle.Text);
        _dragPreview.AddChild(_dragPreviewLabel);
        AddChild(_dragPreview);

        _dragGhost = new DirectionalAnimatedSprite
        {
            Name = "DragGhost",
            Visible = false,
            ZIndex = 100,
            Modulate = new Color(1.0f, 1.0f, 1.0f, 0.55f)
        };
        AddChild(_dragGhost);
    }

    private void CreateUnitSelectionPanel()
    {
        _unitSelectionPanel = new PanelContainer
        {
            Name = "UnitSelectionPanel",
            Visible = false,
            Position = new Vector2(906, 402),
            CustomMinimumSize = new Vector2(342, 152),
            ZIndex = 90
        };
        _unitSelectionPanel.AddThemeStyleboxOverride(
            "panel",
            OperationUiStyle.PanelStyle(strong: true, border: OperationUiStyle.Amber));
        var content = new VBoxContainer { Name = "Content" };
        content.AddThemeConstantOverride("separation", 10);
        _unitSelectionLabel = new Label
        {
            Name = "Summary",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        _unitSelectionLabel.AddThemeFontSizeOverride("font_size", 15);
        _unitSelectionLabel.AddThemeColorOverride("font_color", OperationUiStyle.Text);
        _retreatButton = new Button { Name = "RetreatButton", CustomMinimumSize = new Vector2(0, 46) };
        OperationUiStyle.ApplyButton(_retreatButton, OperationUiTone.Danger, fontSize: 15);
        _retreatButton.Pressed += () =>
        {
            if (!string.IsNullOrEmpty(_selectedUnitCharacterId))
                EmitSignal(SignalName.RetreatRequested, _selectedUnitCharacterId);
        };
        content.AddChild(_unitSelectionLabel);
        content.AddChild(_retreatButton);
        _unitSelectionPanel.AddChild(content);
        AddChild(_unitSelectionPanel);
    }

    private void CreateBattleControls()
    {
        _placementLabel.OffsetRight = 900;
        var controls = new HBoxContainer
        {
            Name = "BattleControls",
            Position = new Vector2(914, 51),
            CustomMinimumSize = new Vector2(332, 38)
        };
        controls.AddThemeConstantOverride("separation", 8);
        _pauseButton = new Button { Name = "PauseButton", Text = "일시정지", CustomMinimumSize = new Vector2(96, 36) };
        _speedButton = new Button { Name = "SpeedButton", Text = "1×", CustomMinimumSize = new Vector2(72, 36) };
        _firstWaveButton = new Button { Name = "FirstWaveButton", Text = "웨이브 시작", CustomMinimumSize = new Vector2(148, 36) };
        OperationUiStyle.ApplyButton(_pauseButton, fontSize: 14);
        OperationUiStyle.ApplyButton(_speedButton, OperationUiTone.Primary, fontSize: 14);
        OperationUiStyle.ApplyButton(_firstWaveButton, OperationUiTone.Success, fontSize: 14);
        _pauseButton.Pressed += () => EmitSignal(SignalName.PauseRequested);
        _speedButton.Pressed += () => EmitSignal(SignalName.BattleSpeedRequested);
        _firstWaveButton.Pressed += () => EmitSignal(SignalName.FirstWaveStartRequested);
        controls.AddChild(_pauseButton);
        controls.AddChild(_speedButton);
        controls.AddChild(_firstWaveButton);
        AddChild(controls);
    }

    private void ConfigureDragGhost(UnitVisualDefinition visual)
    {
        UnitVisualConfigurator.Apply(_dragGhost, visual);
        _dragGhostOffset = visual.SpritePosition;
        if (_dragGhost.Material is ShaderMaterial material)
            material.SetShaderParameter("opacity", 0.55f);
        ApplyDragGhostTint(OperationUiStyle.Amber);
        _dragGhost.Play(UnitVisualConfigurator.IdleAnimation);
    }

    private void ApplyDragGhostTint(Color color)
    {
        Color blended = Colors.White.Lerp(color, 0.28f);
        _dragGhost.Modulate = new Color(blended.R, blended.G, blended.B, 0.55f);
        if (_dragGhost.Material is not ShaderMaterial material) return;
        material.SetShaderParameter("tint_color", color);
        material.SetShaderParameter("tint_strength", 0.28f);
    }

    private void ApplyOperationStyle()
    {
        OperationUiStyle.ApplyPanel(GetNode<Panel>("TopPanel"), strong: true);
        OperationUiStyle.ApplyPanel(GetNode<Panel>("DeploymentPanel"), strong: true, border: OperationUiStyle.Teal);
        OperationUiStyle.ApplyPanel(_resultPanel, strong: true, border: OperationUiStyle.Amber);
        OperationUiStyle.ApplyButton(_resultSyncRetryButton, OperationUiTone.Danger, fontSize: 15);
        OperationUiStyle.ApplyButton(_restartButton, OperationUiTone.Primary, fontSize: 17);
        OperationUiStyle.ApplyButton(_stageListButton, fontSize: 17);
    }
}
