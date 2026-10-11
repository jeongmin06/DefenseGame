using DefenseGame.Client.Data;
using DefenseGame.Client.Skills;
using DefenseGame.Client.Network;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DefenseGame.Client.UI;

public partial class SquadFormationScreen : Node2D
{
    [Export] public PlayerSkillDefaults Defaults { get; set; } = null!;

    public int OwnedCharacterCount => Defaults?.CatProfiles.Count ?? 0;
    public int SelectedCount => _selectedIds.Count;
    public int Capacity { get; private set; }
    public int MaxSquadUnits => StageSelectionState.SelectedStage?.MaxSquadUnits ?? 0;
    public string StatusMessage => _statusLabel?.Text ?? "";
    public string LoadStatusCode => _usingLocalTestStorage ? _loadResult?.StatusCode ?? "" : _remoteLoadStatus;
    public string SaveStatusCode => _lastSaveStatus;
    public int ServerRevision => _serverRevision;
    public bool IsServerReady => _usingLocalTestStorage || _serverReady;

    private readonly StageSquadPresetStore _store = new();
    private readonly StageSquadServerClient _serverClient = new();
    private readonly CancellationTokenSource _serverCancellation = new();
    private readonly StageSquadValidator _validator = new();
    private readonly Dictionary<string, CatProfile> _profilesById = new(StringComparer.Ordinal);
    private Godot.Collections.Array<StageSquadPreset> _presets = new();
    private readonly Godot.Collections.Array<string> _selectedIds = new();
    private StageSquadPresetLoadResult? _loadResult;
    private string _lastSaveStatus = "";
    private bool _requiresRecoverySave;
    private bool _usingLocalTestStorage;
    private bool _serverReady;
    private bool _serverBusy;
    private int _serverRevision;
    private string _remoteLoadStatus = "Loading";
    private VBoxContainer _ownedList = null!;
    private VBoxContainer _selectedList = null!;
    private Label _countsLabel = null!;
    private Label _statusLabel = null!;
    private Button _continueButton = null!;

    public override void _Ready()
    {
        _ownedList = GetNode<VBoxContainer>("UI/MainPanel/OwnedList");
        _selectedList = GetNode<VBoxContainer>("UI/MainPanel/SelectedList");
        _countsLabel = GetNode<Label>("UI/MainPanel/CountsLabel");
        _statusLabel = GetNode<Label>("UI/MainPanel/StatusLabel");
        _continueButton = GetNode<Button>("UI/MainPanel/Actions/ContinueButton");
        GetNode<Button>("UI/MainPanel/Actions/BackButton").Pressed += ReturnToStageList;
        _continueButton.Pressed += () => ContinueToSkills();

        StageDefinition? stage = StageSelectionState.SelectedStage;
        GetNode<Label>("UI/StageLabel").Text = stage is null
            ? "선택된 스테이지 없음"
            : $"{stage.DisplayName}  //  {stage.Id}";
        Load(stage);
        Refresh();
        if (!_usingLocalTestStorage && !_serverReady && stage is not null)
            _ = LoadFromServerAsync(stage);
        QueueRedraw();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!@event.IsActionPressed("ui_cancel")) return;
        GetViewport().SetInputAsHandled();
        ReturnToStageList();
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(0, 0, 1280, 720), new Color("102a24"));
        DrawCircle(new Vector2(1135, 105), 190, new Color("6d3425"));
        DrawCircle(new Vector2(1135, 105), 145, new Color("d98e3d"));
        DrawLine(new Vector2(850, 0), new Vector2(1280, 430), new Color("2f5f43"), 76);
    }

    public string[] GetSelectedCharacterIds() => _selectedIds.ToArray();

    public bool ToggleCharacter(string characterId)
    {
        if (!_profilesById.ContainsKey(characterId)) return false;
        int index = _selectedIds.IndexOf(characterId);
        if (index >= 0) _selectedIds.RemoveAt(index);
        else if (_selectedIds.Count < Capacity) _selectedIds.Add(characterId);
        else
        {
            _statusLabel.Text = $"수정 필요 // 고양이는 최대 {Capacity}마리까지 편성할 수 있습니다.";
            return false;
        }
        Refresh();
        return true;
    }

    public bool MoveSelectedUp(string characterId) => Move(characterId, -1);
    public bool MoveSelectedDown(string characterId) => Move(characterId, 1);

    public bool RemoveSelectedAt(int index)
    {
        if (index < 0 || index >= _selectedIds.Count) return false;
        _selectedIds.RemoveAt(index);
        Refresh();
        return true;
    }

    public bool ContinueToSkills()
    {
        StageDefinition? stage = StageSelectionState.SelectedStage;
        if (stage is null)
        {
            _statusLabel.Text = "수정 필요 // 스테이지를 먼저 선택하세요.";
            return false;
        }
        StageSquadValidationResult validation = Validate(stage);
        if (!validation.IsValid)
        {
            _statusLabel.Text = "수정 필요 // " + validation.Issues[0].Message;
            RefreshLists();
            return false;
        }
        if (!_usingLocalTestStorage)
        {
            if (!_serverReady || _serverBusy)
            {
                _statusLabel.Text = "수정 필요 // 서버의 편성 정보가 아직 준비되지 않았습니다.";
                return false;
            }
            _ = SaveToServerAndContinueAsync(stage, validation);
            return true;
        }

        StageSquadPreset? preset = _presets.FirstOrDefault(item => item.StageId == stage.Id);
        if (preset is null)
        {
            preset = new StageSquadPreset { StageId = stage.Id };
            _presets.Add(preset);
        }
        preset.CharacterIds = new Godot.Collections.Array<string>(_selectedIds);
        preset.UpdatedAtUtc = DateTimeOffset.UtcNow.ToString("O");
        StageSquadPresetSaveResult save = _requiresRecoverySave
            ? _store.SaveAfterRecovery(_presets)
            : _store.Save(_presets);
        _lastSaveStatus = save.StatusCode;
        if (save.Status != StageSquadPresetSaveStatus.Saved)
        {
            _statusLabel.Text = $"수정 필요 // SAVE {save.StatusCode}: {save.Message}";
            return false;
        }
        _requiresRecoverySave = false;
        ApplySelectionAndContinue(stage, validation);
        return true;
    }

    public void ReturnToStageList()
    {
        StageSelectionState.ClearStage();
        GetTree().ChangeSceneToFile("res://scenes/stage_select.tscn");
    }

    private void Load(StageDefinition? stage)
    {
        foreach (CatProfile profile in Defaults.CatProfiles) _profilesById[profile.CharacterId] = profile;
        _usingLocalTestStorage = !string.IsNullOrWhiteSpace(StageSelectionState.StageSquadStoragePathOverride);
        if (_usingLocalTestStorage)
        {
            _store.ConfigureStoragePath(StageSelectionState.StageSquadStoragePathOverride);
            _loadResult = _store.Load();
            _presets = _loadResult.Presets;
            _requiresRecoverySave = _loadResult.RequiresExplicitOverwrite;
        }
        if (stage is null) return;
        Capacity = stage.MaxSquadUnits;
        if (StageSelectionState.ActiveSquadStageId == stage.Id)
        {
            _selectedIds.AddRange(StageSelectionState.SelectedCharacterIds);
            _serverRevision = StageSelectionState.ActiveSquadRevision;
            _serverReady = true;
            _remoteLoadStatus = "Session";
        }
        else if (_usingLocalTestStorage)
        {
            StageSquadPreset? stored = _presets.FirstOrDefault(item => item.StageId == stage.Id);
            if (stored is not null) _selectedIds.AddRange(stored.CharacterIds);
            else foreach (CatProfile profile in Defaults.CatProfiles.Take(Capacity)) _selectedIds.Add(profile.CharacterId);
        }
        else foreach (CatProfile profile in Defaults.CatProfiles.Take(Capacity)) _selectedIds.Add(profile.CharacterId);
    }

    private bool Move(string characterId, int direction)
    {
        int index = _selectedIds.IndexOf(characterId), target = index + direction;
        if (index < 0 || target < 0 || target >= _selectedIds.Count) return false;
        string other = _selectedIds[target];
        _selectedIds[target] = characterId;
        _selectedIds[index] = other;
        Refresh();
        return true;
    }

    private StageSquadValidationResult Validate(StageDefinition stage) => _validator.Validate(new StageSquadAllocation
    {
        StageId = stage.Id,
        MaxSquadUnits = stage.MaxSquadUnits,
        OwnedProfiles = Defaults.CatProfiles,
        CharacterIds = new Godot.Collections.Array<string>(_selectedIds)
    });

    private void Refresh()
    {
        StageDefinition? stage = StageSelectionState.SelectedStage;
        StageSquadValidationResult? validation = stage is null ? null : Validate(stage);
        _countsLabel.Text = $"출전  {SelectedCount}/{MaxSquadUnits}   //   보유 {OwnedCharacterCount}";
        _continueButton.Disabled = validation is null || !validation.IsValid || _serverBusy
            || (!_usingLocalTestStorage && !_serverReady)
            || _loadResult?.Status == StageSquadPresetLoadStatus.MigrationRequired;
        _statusLabel.Text = !_usingLocalTestStorage && _serverBusy
            ? (_lastSaveStatus == "Saving" ? "서버 // 편성을 저장하고 있습니다..." : "서버 // 편성을 불러오고 있습니다...")
            : !_usingLocalTestStorage && !_serverReady
                ? $"수정 필요 // 서버 {_remoteLoadStatus}: 서버를 실행한 뒤 이 화면을 다시 열어주세요."
            : _loadResult?.Status == StageSquadPresetLoadStatus.MigrationRequired
            ? "수정 필요 // 저장된 편성을 변경하려면 데이터 이전이 필요합니다."
            : validation is { IsValid: false } ? "수정 필요 // " + validation.Issues[0].Message
            : _loadResult?.Status == StageSquadPresetLoadStatus.RecoveredFromBackup
                ? "수정 필요 // 마지막 정상 백업을 복구했습니다. 계속하기 전에 편성을 확인하세요."
            : _loadResult?.Status == StageSquadPresetLoadStatus.Corrupt
                ? "수정 필요 // 저장된 편성 데이터가 손상되었습니다. 현재 편성을 확인하세요."
            : "편성 준비 완료 // 표시된 순서대로 전투 배치 카드가 생성됩니다.";
        RefreshLists();
    }

    private void RefreshLists()
    {
        Clear(_ownedList); Clear(_selectedList);
        foreach (CatProfile profile in Defaults.CatProfiles)
        {
            string id = profile.CharacterId;
            bool selected = _selectedIds.Contains(id);
            var button = new Button
            {
                Name = $"Owned_{id}",
                Text = $"{(selected ? "[X]" : "[ ]")}  {profile.DisplayName}  //  {KoreanUiText.Role(profile.Unit?.Role ?? UnitRole.Ranged)} · {id}",
                CustomMinimumSize = new Vector2(450, 52), Alignment = HorizontalAlignment.Left,
                TooltipText = UiLocalization.Description(profile.Description)
            };
            button.Pressed += () => ToggleCharacter(id);
            Style(button, selected ? "536344" : "3b4938", selected ? "ffd166" : "8f6d38");
            _ownedList.AddChild(button);
        }
        for (int index = 0; index < _selectedIds.Count; index++)
        {
            string id = _selectedIds[index];
            string label = _profilesById.TryGetValue(id, out CatProfile? profile) ? profile.DisplayName : "누락된 캐릭터";
            var row = new HBoxContainer { Name = $"Selected_{index}" };
            var text = new Label { Text = $"{index + 1:00}  {label}  //  {id}", CustomMinimumSize = new Vector2(330, 42) };
            text.AddThemeColorOverride("font_color", new Color(profile is null ? "ff8e78" : "fff0c2"));
            var up = new Button { Text = "▲", Disabled = index == 0, CustomMinimumSize = new Vector2(48, 40) };
            var down = new Button { Text = "▼", Disabled = index == _selectedIds.Count - 1, CustomMinimumSize = new Vector2(48, 40) };
            var remove = new Button { Text = "×", CustomMinimumSize = new Vector2(48, 40) };
            int slotIndex = index;
            up.Pressed += () => MoveSelectedUp(id); down.Pressed += () => MoveSelectedDown(id);
            remove.Pressed += () => RemoveSelectedAt(slotIndex);
            row.AddChild(text); row.AddChild(up); row.AddChild(down); row.AddChild(remove); _selectedList.AddChild(row);
        }
    }

    private static void Clear(Node parent)
    {
        foreach (Node child in parent.GetChildren()) { parent.RemoveChild(child); child.QueueFree(); }
    }

    private static void Style(Button button, string background, string border)
    {
        button.AddThemeFontSizeOverride("font_size", 15);
        button.AddThemeColorOverride("font_color", new Color("fff0c2"));
        button.AddThemeStyleboxOverride("normal", new StyleBoxFlat
        {
            BgColor = new Color(background), BorderColor = new Color(border),
            BorderWidthLeft = 2, BorderWidthTop = 2, BorderWidthRight = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6, CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
            ContentMarginLeft = 14
        });
    }

    private async Task LoadFromServerAsync(StageDefinition stage)
    {
        _serverBusy = true;
        _remoteLoadStatus = "Loading";
        Refresh();
        StageSquadServerResult result = await _serverClient.LoadAsync(
            ServerBaseUrl(), PlayerId(), stage.Id, _serverCancellation.Token);
        if (!IsInsideTree()) return;
        _serverBusy = false;
        if (!result.IsSuccess || result.Payload is null)
        {
            _remoteLoadStatus = result.ErrorCode;
            _serverReady = false;
            Refresh();
            return;
        }
        _serverRevision = result.Payload.Revision;
        _remoteLoadStatus = "Loaded";
        _serverReady = true;
        if (result.Payload.CharacterIds.Length > 0)
        {
            _selectedIds.Clear();
            _selectedIds.AddRange(result.Payload.CharacterIds);
        }
        Refresh();
    }

    private async Task SaveToServerAndContinueAsync(StageDefinition stage, StageSquadValidationResult validation)
    {
        _serverBusy = true;
        _lastSaveStatus = "Saving";
        Refresh();
        StageSquadServerResult result = await _serverClient.SaveAsync(
            ServerBaseUrl(), PlayerId(), stage.Id, _selectedIds.ToArray(), _serverRevision, _serverCancellation.Token);
        if (!IsInsideTree()) return;
        _serverBusy = false;
        if (!result.IsSuccess || result.Payload is null)
        {
            _lastSaveStatus = result.ErrorCode;
            if (result.IsConflict && result.Payload is not null) _serverRevision = result.Payload.Revision;
            _statusLabel.Text = result.IsConflict
                ? "수정 필요 // 서버 저장 충돌이 발생했습니다. 편성을 확인하고 다시 저장하세요."
                : $"수정 필요 // 서버 {result.ErrorCode}: {result.Message}";
            RefreshLists();
            _continueButton.Disabled = false;
            return;
        }
        _serverRevision = result.Payload.Revision;
        _lastSaveStatus = "Saved";
        ApplySelectionAndContinue(stage, validation);
    }

    private void ApplySelectionAndContinue(StageDefinition stage, StageSquadValidationResult validation)
    {
        StageSelectionState.SelectedCharacterIds = new Godot.Collections.Array<string>(_selectedIds);
        StageSelectionState.SelectedProfiles = new Godot.Collections.Array<CatProfile>(validation.SelectedProfiles);
        StageSelectionState.ActiveSquadStageId = stage.Id;
        StageSelectionState.ActiveSquadRevision = _serverRevision;
        GetTree().ChangeSceneToFile("res://scenes/skill_loadout.tscn");
    }

    private static string ServerBaseUrl() => (string)ProjectSettings.GetSetting(
        "defense_game/server/base_url", "http://127.0.0.1:5080");

    private static string PlayerId() => (string)ProjectSettings.GetSetting(
        "defense_game/player/development_user_id", "local-development-user");

    public override void _ExitTree()
    {
        _serverCancellation.Cancel();
        _serverCancellation.Dispose();
    }
}
