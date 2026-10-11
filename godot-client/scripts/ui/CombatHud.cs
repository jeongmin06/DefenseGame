using Godot;
using DefenseGame.Client.Combat;
using DefenseGame.Client.Data;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DefenseGame.Client.UI;

public partial class CombatHud : CanvasLayer
{
    [Signal] public delegate void CharacterSelectedEventHandler(string characterId);
    [Signal] public delegate void CharacterDroppedEventHandler(string characterId, Vector2 globalPosition);
    [Signal] public delegate void RetryRequestedEventHandler();
    [Signal] public delegate void StageListRequestedEventHandler();
    private readonly Dictionary<string, Button> _deploymentCards = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CatProfile> _profiles = new(StringComparer.Ordinal);
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
    private Button _restartButton = null!;
    private Button _stageListButton = null!;
    private Label _title = null!;
    private PanelContainer _dragPreview = null!;
    private Label _dragPreviewLabel = null!;
    private string _dragCandidateId = "";
    private string _draggedCharacterId = "";
    private Vector2 _dragStart;

    public int DeploymentCardCount => _deploymentCards.Count;
    public string[] DeploymentCardCharacterIds => _deploymentCards.Keys.ToArray();
    public bool IsDraggingCharacter => !string.IsNullOrEmpty(_draggedCharacterId);
    public string DraggedCharacterId => _draggedCharacterId;

    public override void _Ready()
    {
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
        _restartButton = GetNode<Button>("ResultPanel/RestartButton");
        _restartButton.Pressed += () => EmitSignal(SignalName.RetryRequested);
        _stageListButton = GetNode<Button>("ResultPanel/StageListButton");
        _stageListButton.Pressed += () => EmitSignal(SignalName.StageListRequested);
        CreateDragPreview();
    }

    public override void _Input(InputEvent input)
    {
        if (input is InputEventMouseMotion motion && !string.IsNullOrEmpty(_dragCandidateId))
        {
            if (!IsDraggingCharacter && motion.GlobalPosition.DistanceTo(_dragStart) >= 12.0f)
                BeginCharacterDrag(_dragCandidateId, motion.GlobalPosition);
            if (IsDraggingCharacter) MoveDragPreview(motion.GlobalPosition);
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
        _title.Text = $"{displayName}  //  {id}";
    }

    public void ConfigureDeploymentCards(IEnumerable<CatProfile> profiles)
    {
        foreach (Node child in _deploymentCardList.GetChildren()) child.QueueFree();
        _deploymentCards.Clear();
        _profiles.Clear();
        foreach (CatProfile profile in profiles)
        {
            if (profile.Unit is null) continue;
            string characterId = profile.CharacterId;
            var button = new Button
            {
                Name = $"Card_{characterId}",
                CustomMinimumSize = new Vector2(220, 66),
                Alignment = HorizontalAlignment.Left
            };
            button.AddThemeFontSizeOverride("font_size", 15);
            button.AddThemeColorOverride("font_color", new Color("fff0c2"));
            button.AddThemeStyleboxOverride("normal", CardStyle("3b4938", "8f6d38"));
            button.AddThemeStyleboxOverride("hover", CardStyle("536344", "f1bb55"));
            button.AddThemeStyleboxOverride("focus", CardStyle("536344", "fff0c2"));
            button.TooltipText = UiLocalization.Description(profile.Description);
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
        float deploymentPoints)
    {
        var remaining = new HashSet<string>(remainingCharacterIds, StringComparer.Ordinal);
        foreach ((string characterId, Button button) in _deploymentCards)
        {
            CatProfile profile = _profiles[characterId];
            UnitDefinition unit = profile.Unit!;
            bool available = remaining.Contains(characterId);
            bool affordable = available && deploymentPoints >= unit.DeploymentCost;
            bool selected = affordable && characterId == selectedCharacterId;
            button.Disabled = !affordable;
            button.Text = available
                ? $"{(selected ? "▶ " : "  ")}{profile.DisplayName}\n  {KoreanUiText.Role(unit.Role)} · 비용 {unit.DeploymentCost}{(affordable ? "" : " · 부족")}\n  {characterId}"
                : $"✓ {profile.DisplayName}\n  {characterId} · 배치 완료";
            button.Modulate = selected ? Colors.White : affordable ? new Color("d6dfca")
                : available ? new Color("a08362") : new Color("788275");
            if (!available && (_dragCandidateId == characterId || _draggedCharacterId == characterId))
                CancelCharacterDrag();
        }
    }

    public void UpdateDeploymentPoints(int current, int maximum)
    {
        _deploymentPointsLabel.Text = $"배치 포인트 {current} / {maximum}";
    }

    public void UpdatePlacement(CatProfile? selectedProfile, int remainingCount)
    {
        if (remainingCount == 0)
        {
            _placementLabel.Text = "모든 고양이 배치 완료";
            return;
        }
        if (selectedProfile?.Unit is null)
        {
            _placementLabel.Text = "배치 포인트 회복 대기";
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
        _placementLabel.Text = "전투 종료";
        foreach (Button button in _deploymentCards.Values) button.Disabled = true;
        _resultPanel.Visible = true;
        _resultTitle.Text = victory ? "작전 성공" : "기지 함락";
        _resultTitle.Modulate = victory ? Rgb(255, 209, 102) : Rgb(255, 107, 94);
        _resultDetail.Text = $"처치 {defeated}  /  탈출 {escaped}";
        _restartButton.GrabFocus();
    }

    public bool IsDeploymentCardDisabled(string characterId) =>
        _deploymentCards.TryGetValue(characterId, out Button? button) && button.Disabled;

    public string GetDeploymentCardText(string characterId) =>
        _deploymentCards.TryGetValue(characterId, out Button? button) ? button.Text : "";

    public bool AreAllDeploymentCardsDisabled() => _deploymentCards.Values.All(button => button.Disabled);

    public bool BeginCharacterDrag(string characterId, Vector2 globalPosition)
    {
        if (!_deploymentCards.TryGetValue(characterId, out Button? button) || button.Disabled
            || !_profiles.TryGetValue(characterId, out CatProfile? profile) || profile.Unit is null)
            return false;
        _dragCandidateId = characterId;
        _draggedCharacterId = characterId;
        _dragPreviewLabel.Text = $"{profile.DisplayName}\n{KoreanUiText.Role(profile.Unit.Role)} · 비용 {profile.Unit.DeploymentCost}";
        _dragPreview.Visible = true;
        MoveDragPreview(globalPosition);
        EmitSignal(SignalName.CharacterSelected, characterId);
        return true;
    }

    public void CancelCharacterDrag()
    {
        _dragCandidateId = "";
        _draggedCharacterId = "";
        if (_dragPreview is not null) _dragPreview.Visible = false;
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

    private void MoveDragPreview(Vector2 globalPosition) =>
        _dragPreview.Position = globalPosition + new Vector2(18, -66);

    private void CreateDragPreview()
    {
        _dragPreview = new PanelContainer
        {
            Name = "DragPreview",
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ZIndex = 100,
            CustomMinimumSize = new Vector2(180, 52)
        };
        _dragPreview.AddThemeStyleboxOverride("panel", CardStyle("3b4938", "f1bb55"));
        _dragPreviewLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _dragPreviewLabel.AddThemeFontSizeOverride("font_size", 15);
        _dragPreviewLabel.AddThemeColorOverride("font_color", new Color("fff0c2"));
        _dragPreview.AddChild(_dragPreviewLabel);
        AddChild(_dragPreview);
    }

    private static StyleBoxFlat CardStyle(string background, string border) => new()
    {
        BgColor = new Color(background), BorderColor = new Color(border),
        BorderWidthLeft = 2, BorderWidthTop = 2, BorderWidthRight = 2, BorderWidthBottom = 2,
        CornerRadiusTopLeft = 5, CornerRadiusTopRight = 5, CornerRadiusBottomLeft = 5, CornerRadiusBottomRight = 5,
        ContentMarginLeft = 12
    };

    private static Color Rgb(byte red, byte green, byte blue, byte alpha = 255)
    {
        return new Color(red / 255.0f, green / 255.0f, blue / 255.0f, alpha / 255.0f);
    }
}
