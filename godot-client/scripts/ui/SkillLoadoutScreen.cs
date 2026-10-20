using DefenseGame.Client.Data;
using DefenseGame.Client.Network;
using DefenseGame.Client.Skills;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DefenseGame.Client.UI;

public partial class SkillLoadoutScreen : Node2D
{
    [Export] public SkillCatalog Catalog { get; set; } = null!;
    [Export] public PlayerSkillDefaults Defaults { get; set; } = null!;

    public int CandidateCount => _supportById.Count;
    public int CharacterCount => _selectedProfiles.Count;
    public string SelectedCharacterId => _selectedCharacterId;
    public int SelectedSupportCount => CurrentPreset?.SupportSkillIds.Count ?? 0;
    public int UsedPoints { get; private set; }
    public int UsablePoints { get; private set; }
    public int RemainingPoints => UsablePoints - UsedPoints;
    public int UsedLinkCores => SelectedSupportCount;
    public int RemainingLinkCores => RemainingPoints;
    public string StatusMessage => _statusLabel.Text;
    public string LoadStatusCode => _usingLocalTestStorage ? _loadResult?.StatusCode ?? "" : _remoteLoadStatus;
    public string SaveStatusCode => _lastSaveStatus;
    public int ServerRevision => _serverRevision;
    public bool IsServerReady => _usingLocalTestStorage || _serverReady;
    public bool MigrationAvailable => !_usingLocalTestStorage && _serverReady
        && _serverRevision == 0 && _profileStore.HasSavedDocument;

    private readonly Dictionary<string, SkillDefinition> _skillsById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SkillDefinition> _supportById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CatProfile> _profilesById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CatSkillPreset> _squadPresetsById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Button> _characterButtons = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Button> _activeButtons = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Button> _supportButtons = new(StringComparer.Ordinal);
    private readonly List<CatProfile> _selectedProfiles = new();
    private readonly SquadSkillValidator _validator = new();
    private readonly SkillProfileStore _profileStore = new();
    private readonly SkillProfileServerClient _serverClient = new();
    private readonly CancellationTokenSource _serverCancellation = new();

    private PlayerSkillProgress _progress = null!;
    private Godot.Collections.Array<CatSkillPreset> _storedPresets = new();
    private SkillProfileLoadResult? _loadResult;
    private SquadSkillValidationResult _validation = new();
    private string _selectedCharacterId = "";
    private string _lastSaveStatus = "";
    private bool _requiresRecoverySave;
    private bool _usingLocalTestStorage;
    private bool _serverReady;
    private bool _serverBusy;
    private bool _serverDirty;
    private int _serverRevision;
    private string _remoteLoadStatus = "Loading";
    private string _pendingFocusKey = "";

    private HBoxContainer _characterList = null!;
    private HBoxContainer _activeList = null!;
    private VBoxContainer _candidateList = null!;
    private Label _activeLabel = null!;
    private Label _budgetLabel = null!;
    private Label _statusLabel = null!;
    private Button _saveButton = null!;
    private Button _migrationButton = null!;
    private Button _startButton = null!;

    private CatSkillPreset? CurrentPreset => _squadPresetsById.GetValueOrDefault(_selectedCharacterId);

    public override void _Ready()
    {
        _characterList = GetNode<HBoxContainer>("UI/MainPanel/CharacterScroll/CharacterList");
        _activeList = GetNode<HBoxContainer>("UI/MainPanel/ActiveCard/ActiveList");
        _candidateList = GetNode<VBoxContainer>("UI/MainPanel/SupportScroll/CandidateList");
        _activeLabel = GetNode<Label>("UI/MainPanel/ActiveCard/ActiveLabel");
        _budgetLabel = GetNode<Label>("UI/MainPanel/BudgetLabel");
        _statusLabel = GetNode<Label>("UI/MainPanel/StatusLabel");
        _saveButton = GetNode<Button>("UI/MainPanel/Actions/SaveButton");
        _migrationButton = GetNode<Button>("UI/MainPanel/Actions/MigrationButton");
        _startButton = GetNode<Button>("UI/MainPanel/Actions/StartButton");
        Button backButton = GetNode<Button>("UI/MainPanel/Actions/BackButton");
        backButton.Pressed += ReturnToFormation;
        _saveButton.Pressed += () => SaveProfile();
        _migrationButton.Pressed += () => MigrateLocalProfile();
        _startButton.Pressed += () => StartBattle();
        OperationUiStyle.ApplyButton(backButton);
        OperationUiStyle.ApplyButton(_saveButton, OperationUiTone.Success);
        OperationUiStyle.ApplyButton(_migrationButton, OperationUiTone.Primary);
        OperationUiStyle.ApplyButton(_startButton, OperationUiTone.Primary, fontSize: 16);

        StageDefinition? stage = StageSelectionState.SelectedStage;
        GetNode<Label>("UI/StageLabel").Text = stage is null
            ? "선택된 스테이지 없음"
            : stage.DisplayName;

        LoadCatalog();
        LoadProfile();
        BuildCharacterButtons();
        _selectedCharacterId = _selectedProfiles.FirstOrDefault()?.CharacterId ?? "";
        Refresh();
        _characterButtons.GetValueOrDefault(_selectedCharacterId)?.GrabFocus();
        if (!_usingLocalTestStorage) _ = LoadFromServerAsync();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("ui_cancel"))
        {
            GetViewport().SetInputAsHandled();
            ReturnToStageList();
        }
    }

    public bool SelectCharacter(string characterId)
    {
        if (!_profilesById.ContainsKey(characterId)) return false;
        _selectedCharacterId = characterId;
        _pendingFocusKey = $"character:{characterId}";
        Refresh();
        return true;
    }

    public bool TrySelectActive(string id)
    {
        if (!CanEditServerProfile()) return false;
        CatSkillPreset? preset = CurrentPreset;
        if (preset is null || !_skillsById.TryGetValue(id, out SkillDefinition? skill)
            || skill.Role != SkillRole.Active || !_progress.OwnedSkillIds.Contains(id))
        {
            ShowTransient($"수정 필요 // 액티브 스킬 '{id}'을 선택할 수 없습니다.", true);
            return false;
        }
        preset.ActiveSkillId = id;
        MarkEdited(preset);
        _pendingFocusKey = $"active:{id}";
        Refresh();
        return true;
    }

    public bool TryToggleSupport(string id)
    {
        if (!CanEditServerProfile()) return false;
        CatSkillPreset? preset = CurrentPreset;
        if (preset is null || !_supportById.TryGetValue(id, out SkillDefinition? support))
        {
            ShowTransient($"수정 필요 // 알 수 없는 보조 스킬입니다: '{id}'.", true);
            return false;
        }
        int existingIndex = preset.SupportSkillIds.IndexOf(id);
        if (existingIndex >= 0)
        {
            preset.SupportSkillIds.RemoveAt(existingIndex);
            preset.AllocatedPoints = preset.SupportSkillIds.Count;
            MarkEdited(preset);
            _pendingFocusKey = $"support:{id}";
            Refresh($"{support.DisplayName} 스킬을 해제했습니다.");
            return true;
        }
        if (preset.SupportSkillIds.Count >= SquadSkillValidator.MaxSupportsPerCat)
        {
            ShowTransient($"수정 필요 // 고양이 한 마리는 보조 스킬을 최대 {SquadSkillValidator.MaxSupportsPerCat}개 장착할 수 있습니다.", true);
            return false;
        }

        preset.SupportSkillIds.Add(id);
        preset.AllocatedPoints = preset.SupportSkillIds.Count;
        SquadSkillValidationResult proposed = ValidateAllocation();
        bool rejected = proposed.Issues.Any(issue => issue.CharacterId == _selectedCharacterId)
            || proposed.Issues.Any(issue => issue.Error == SquadSkillError.SquadBudgetExceeded);
        if (rejected)
        {
            preset.SupportSkillIds.RemoveAt(preset.SupportSkillIds.Count - 1);
            preset.AllocatedPoints = preset.SupportSkillIds.Count;
            _validation = ValidateAllocation();
            ShowTransient("수정 필요 // " + FirstRelevantMessage(proposed), true);
            return false;
        }
        MarkEdited(preset);
        _pendingFocusKey = $"support:{id}";
        Refresh($"{support.DisplayName} 스킬을 장착했습니다.");
        return true;
    }

    public bool RemoveSupportAt(int index)
    {
        if (!CanEditServerProfile()) return false;
        CatSkillPreset? preset = CurrentPreset;
        if (preset is null || index < 0 || index >= preset.SupportSkillIds.Count) return false;
        preset.SupportSkillIds.RemoveAt(index);
        preset.AllocatedPoints = preset.SupportSkillIds.Count;
        MarkEdited(preset);
        _pendingFocusKey = $"character:{_selectedCharacterId}";
        Refresh("잘못된 보조 스킬 슬롯을 제거했습니다.");
        return true;
    }

    public bool IsSupportSelected(string id) => CurrentPreset?.SupportSkillIds.Contains(id) ?? false;

    public bool IsRepairRequired(string characterId) =>
        _validation.Issues.Any(issue => issue.CharacterId == characterId)
        || (_validation.Issues.Any(issue => string.IsNullOrEmpty(issue.CharacterId)) && _profilesById.ContainsKey(characterId));

    public string[] GetSupportIds(string characterId) => _squadPresetsById.TryGetValue(characterId, out CatSkillPreset? preset)
        ? preset.SupportSkillIds.ToArray() : [];

    public bool SaveProfile()
    {
        if (!_usingLocalTestStorage)
        {
            if (!_serverReady || _serverBusy)
            {
                ShowTransient("수정 필요 // 서버의 스킬 프리셋이 아직 준비되지 않았습니다.", true);
                return false;
            }
            _validation = ValidateAllocation();
            if (!_validation.IsValid)
            {
                Refresh();
                return false;
            }
            _ = SaveToServerAsync(_storedPresets, false, false);
            return true;
        }

        SkillProfileSaveResult result = _requiresRecoverySave
            ? _profileStore.SaveAfterRecovery(_progress, _storedPresets)
            : _profileStore.Save(_progress, _storedPresets);
        _lastSaveStatus = result.StatusCode;
        if (result.Status == SkillProfileSaveStatus.Saved) _requiresRecoverySave = false;
        ShowTransient(result.Status == SkillProfileSaveStatus.Saved
            ? "프리셋 저장 완료 // 캐릭터 ID별 설정을 저장했습니다."
            : $"수정 필요 // SAVE {result.StatusCode}: {result.Message}", result.Status != SkillProfileSaveStatus.Saved);
        return result.Status == SkillProfileSaveStatus.Saved;
    }

    public bool StartBattle()
    {
        if (StageSelectionState.SelectedStage is null)
        {
            ShowTransient("수정 필요 // 전투 전에 스테이지를 선택하세요.", true);
            return false;
        }
        _validation = ValidateAllocation();
        if (!_validation.IsValid)
        {
            Refresh();
            return false;
        }
        if (!_usingLocalTestStorage)
        {
            if (!_serverReady || _serverBusy)
            {
                ShowTransient("수정 필요 // 서버의 스킬 프리셋이 아직 준비되지 않았습니다.", true);
                return false;
            }
            if (_serverDirty)
            {
                _ = SaveToServerAsync(_storedPresets, false, true);
                return true;
            }
        }
        ApplyLoadoutAndStartBattle();
        return true;
    }

    public bool MigrateLocalProfile()
    {
        if (!MigrationAvailable || _serverBusy)
        {
            ShowTransient("수정 필요 // 현재 계정은 로컬 프리셋을 이전할 수 없습니다.", true);
            return false;
        }
        SkillProfileLoadResult local = _profileStore.Load();
        if (local.Status != SkillProfileLoadStatus.Loaded)
        {
            ShowTransient($"수정 필요 // 로컬 프리셋 {local.StatusCode}: 정상 저장본만 이전할 수 있습니다.", true);
            return false;
        }
        _ = SaveToServerAsync(local.Presets, true, false);
        return true;
    }

    private void ApplyLoadoutAndStartBattle()
    {
        // The battle bridge keeps both the legacy first loadout and per-character loadouts.
        string firstCharacterId = _selectedProfiles.First().CharacterId;
        StageSelectionState.SelectedLoadout = _validation.GetLoadout(firstCharacterId);
        StageSelectionState.SelectedCharacterLoadouts = new Godot.Collections.Array<CharacterSkillLoadout>(_validation.CharacterLoadouts);
        LoadingScreen.EnterBattle(this);
    }

    public void ReturnToFormation()
    {
        GetTree().ChangeSceneToFile("res://scenes/squad_formation.tscn");
    }

    public void ReturnToStageList() => ReturnToFormation();

    private void LoadCatalog()
    {
        foreach (SkillDefinition skill in Catalog.Skills)
        {
            _skillsById.Add(skill.Id, skill);
            if (skill.Role == SkillRole.Support) _supportById.Add(skill.Id, skill);
        }
        if (!_skillsById.Values.Any(skill => skill.Role == SkillRole.Active))
            throw new InvalidOperationException("Skill catalog has no active skill.");
    }

    private void LoadProfile()
    {
        var allProfiles = Defaults.CatProfiles.ToDictionary(profile => profile.CharacterId, StringComparer.Ordinal);
        IEnumerable<string> selectedIds = StageSelectionState.SelectedCharacterIds.Count > 0
            ? StageSelectionState.SelectedCharacterIds
            : Defaults.CatProfiles.Select(profile => profile.CharacterId);
        foreach (string characterId in selectedIds)
            if (allProfiles.TryGetValue(characterId, out CatProfile? selected)) _selectedProfiles.Add(selected);
        _usingLocalTestStorage = !string.IsNullOrWhiteSpace(StageSelectionState.SkillProfileStoragePathOverride);
        if (_usingLocalTestStorage)
        {
            _profileStore.ConfigureStoragePath(StageSelectionState.SkillProfileStoragePathOverride);
            _loadResult = _profileStore.Load();
            _requiresRecoverySave = _loadResult.RequiresExplicitOverwrite;
            ApplyProfile(_loadResult.Progress, _loadResult.Presets);
            return;
        }

        var progress = new PlayerSkillProgress
        {
            PlayerLevel = Defaults.Progress.PlayerLevel,
            UnlockedPoints = Defaults.Progress.UnlockedPoints,
            OwnedSkillIds = new Godot.Collections.Array<string>(Defaults.Progress.OwnedSkillIds)
        };
        var presets = new Godot.Collections.Array<CatSkillPreset>(Defaults.InitialPresets.Select(ClonePreset));
        ApplyProfile(progress, presets);
    }

    private void ApplyProfile(
        PlayerSkillProgress progress,
        Godot.Collections.Array<CatSkillPreset> presets)
    {
        _progress = progress;
        _storedPresets = presets;
        _profilesById.Clear();
        _squadPresetsById.Clear();
        foreach (CatProfile profile in Defaults.CatProfiles)
        {
            CatSkillPreset? preset = _storedPresets.FirstOrDefault(candidate => candidate.CharacterId == profile.CharacterId);
            if (preset is null)
            {
                CatSkillPreset? initial = Defaults.InitialPresets.FirstOrDefault(candidate => candidate.CharacterId == profile.CharacterId);
                preset = initial is null ? new CatSkillPreset
                {
                    CharacterId = profile.CharacterId,
                    ActiveSkillId = "",
                    AllocatedPoints = 0,
                    UpdatedAtUtc = DateTimeOffset.UtcNow.ToString("O")
                } : ClonePreset(initial);
                _storedPresets.Add(preset);
            }
            if (_selectedProfiles.Any(selected => selected.CharacterId == profile.CharacterId))
            {
                _profilesById.Add(profile.CharacterId, profile);
                _squadPresetsById.Add(profile.CharacterId, preset);
            }
        }
    }

    private void BuildCharacterButtons()
    {
        foreach (CatProfile profile in _selectedProfiles)
        {
            string characterId = profile.CharacterId;
            var button = new Button
            {
                Name = $"Character_{characterId}",
                CustomMinimumSize = new Vector2(267, 40),
                Alignment = HorizontalAlignment.Left,
                TooltipText = $"{UiLocalization.Description(profile.Description)}\n관리 ID: {characterId}"
            };
            OperationUiStyle.ApplyButton(button, fontSize: 14, leftPadding: 10);
            button.Pressed += () => SelectCharacter(characterId);
            _characterList.AddChild(button);
            _characterButtons.Add(characterId, button);
        }
    }

    private void RebuildActiveButtons()
    {
        ClearChildren(_activeList);
        _activeButtons.Clear();
        foreach (SkillDefinition skill in Catalog.Skills)
        {
            if (skill.Role != SkillRole.Active) continue;
            string id = skill.Id;
            var button = new Button
            {
                Name = $"Active_{id}", Text = CurrentPreset?.ActiveSkillId == id ? "[장착됨]" : "장착",
                CustomMinimumSize = new Vector2(130, 42),
                Disabled = !_progress.OwnedSkillIds.Contains(id) || (!_usingLocalTestStorage && (!_serverReady || _serverBusy)),
                TooltipText = $"{skill.DisplayName}\n{UiLocalization.Description(skill.Description)}\n관리 ID: {id}"
            };
            bool selected = CurrentPreset?.ActiveSkillId == id;
            OperationUiStyle.ApplyButton(button, OperationUiTone.Primary, selected, fontSize: 14, leftPadding: 10);
            button.Pressed += () => TrySelectActive(id);
            _activeList.AddChild(button);
            _activeButtons[id] = button;
        }
    }

    private void RebuildSupportButtons()
    {
        ClearChildren(_candidateList);
        _supportButtons.Clear();
        CatSkillPreset? preset = CurrentPreset;
        if (preset is null) return;
        var occurrence = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string id in preset.SupportSkillIds) occurrence[id] = occurrence.GetValueOrDefault(id) + 1;

        foreach (SkillDefinition support in Catalog.Skills)
        {
            if (support.Role != SkillRole.Support) continue;
            bool selected = preset.SupportSkillIds.Contains(support.Id);
            bool repair = selected && HasSupportIssue(support.Id);
            string description = UiLocalization.Description(support.Description);
            var button = new Button
            {
                Name = $"Support_{support.Id}",
                Text = $"{(selected ? "[X]" : "[ ]")}  {support.DisplayName}   //   {CompatibilityText(support)}{(repair ? "   //   수정 필요" : "")}\n     {description}",
                CustomMinimumSize = new Vector2(1108, 64), Alignment = HorizontalAlignment.Left,
                TooltipText = $"{description}\n관리 ID: {support.Id}",
                Disabled = !_usingLocalTestStorage && (!_serverReady || _serverBusy)
            };
            OperationUiStyle.ApplyButton(
                button,
                selected ? OperationUiTone.Primary : OperationUiTone.Neutral,
                selected,
                repair,
                15);
            string id = support.Id;
            button.Pressed += () => TryToggleSupport(id);
            _candidateList.AddChild(button);
            _supportButtons[id] = button;
        }

        for (int index = 0; index < preset.SupportSkillIds.Count; index++)
        {
            string id = preset.SupportSkillIds[index];
            bool missing = !_supportById.ContainsKey(id);
            bool duplicate = occurrence[id] > 1 && preset.SupportSkillIds.IndexOf(id) != index;
            bool overLimit = index >= SquadSkillValidator.MaxSupportsPerCat;
            if (!missing && !duplicate && !overLimit) continue;
            int slotIndex = index;
            string reason = missing ? "스킬 누락" : duplicate ? "중복 슬롯" : "슬롯 제한 초과";
            var invalid = new Button
            {
                Name = $"InvalidSupport_{index}",
                Text = $"[X]  {id}   //   수정 필요: {reason}   //   제거",
                CustomMinimumSize = new Vector2(1108, 44), Alignment = HorizontalAlignment.Left
            };
            OperationUiStyle.ApplyButton(invalid, OperationUiTone.Danger, repairRequired: true, fontSize: 14);
            invalid.Pressed += () => RemoveSupportAt(slotIndex);
            _candidateList.AddChild(invalid);
        }
    }

    private void Refresh(string transientMessage = "")
    {
        _validation = ValidateAllocation();
        UsedPoints = _squadPresetsById.Values.Sum(preset => preset.AllocatedPoints);
        UsablePoints = _validation.UsablePoints;
        foreach ((string id, Button button) in _characterButtons)
        {
            CatProfile profile = _profilesById[id];
            bool selected = id == _selectedCharacterId;
            bool repair = IsRepairRequired(id);
            string state = repair ? "수정 필요" : selected ? "선택됨" : "대기";
            int supportCount = _squadPresetsById.GetValueOrDefault(id)?.SupportSkillIds.Count ?? 0;
            button.Text = $"[{state}] {profile.DisplayName}\n{KoreanUiText.Role(profile.Unit?.Role ?? UnitRole.Ranged)} · 보조 {supportCount}/{SquadSkillValidator.MaxSupportsPerCat}";
            OperationUiStyle.ApplyButton(
                button,
                selected ? OperationUiTone.Primary : OperationUiTone.Neutral,
                selected,
                repair,
                14,
                10);
        }

        CatSkillPreset? preset = CurrentPreset;
        string activeId = preset?.ActiveSkillId ?? "";
        string activeName = _skillsById.TryGetValue(activeId, out SkillDefinition? active) ? active.DisplayName : activeId;
        bool activeRepair = HasActiveIssue();
        _activeLabel.Text = $"액티브  //  {(string.IsNullOrEmpty(activeName) ? "누락" : activeName)}\n{(active is null ? activeId : KoreanUiText.Tags(active.Tags, "  ·  "))}{(activeRepair ? "   //   수정 필요" : "")}";
        _activeLabel.TooltipText = active is null ? "" : UiLocalization.Description(active.Description);
        _activeLabel.Modulate = activeRepair ? OperationUiStyle.Red : OperationUiStyle.Text;
        OperationUiStyle.ApplyPanel(
            GetNode<Panel>("UI/MainPanel/ActiveCard"),
            true,
            activeRepair ? OperationUiStyle.Red : OperationUiStyle.Amber);

        StageSkillBudget? budget = StageSelectionState.SelectedStage?.SkillBudget;
        string cap = budget is null || !budget.HasStageCap ? "없음" : budget.StageCap.ToString();
        bool budgetRepair = _validation.Issues.Any(issue => string.IsNullOrEmpty(issue.CharacterId));
        _budgetLabel.Text = $"포인트   사용 가능 {UsablePoints}   사용 {UsedPoints}   남음 {RemainingPoints}   //   스테이지 상한 {cap}{(budgetRepair ? "   //   수정 필요" : "")}";
        _budgetLabel.Modulate = budgetRepair ? OperationUiStyle.Red : OperationUiStyle.Amber;
        OperationUiStyle.ApplyPanel(
            GetNode<Panel>("UI/MainPanel/BudgetPanel"),
            true,
            budgetRepair ? OperationUiStyle.Red : OperationUiStyle.Amber);

        RebuildActiveButtons();
        RebuildSupportButtons();
        string validationMessage = _validation.IsValid ? "모든 고양이 준비 완료" : "수정 필요 // " + FirstRelevantMessage(_validation);
        string loadNote = _usingLocalTestStorage
            ? $"프리셋 {_loadResult?.StatusCode ?? "Unknown"}"
            : _serverBusy
                ? (_lastSaveStatus == "Saving" ? "서버 // 프리셋을 저장하고 있습니다" : "서버 // 프리셋을 불러오고 있습니다")
                : !_serverReady
                    ? $"수정 필요 // 서버 {_remoteLoadStatus}: 서버를 실행한 뒤 이 화면을 다시 열어주세요"
                    : $"서버 프리셋 {_remoteLoadStatus} // revision {_serverRevision}";
        _statusLabel.Text = string.IsNullOrEmpty(transientMessage) ? $"{loadNote}   //   {validationMessage}" : transientMessage;
        bool statusError = !_validation.IsValid || (!_serverBusy && !ServerReadyOrLocal())
            || transientMessage.StartsWith("수정 필요", StringComparison.Ordinal);
        _statusLabel.Modulate = OperationUiStyle.StatusColor(statusError, _serverBusy);
        OperationUiStyle.ApplyPanel(
            GetNode<Panel>("UI/MainPanel/StatusPanel"),
            true,
            OperationUiStyle.StatusColor(statusError, _serverBusy));
        _startButton.Disabled = StageSelectionState.SelectedStage is null || !_validation.IsValid || _serverBusy
            || (!_usingLocalTestStorage && !_serverReady);
        _saveButton.Disabled = _serverBusy || (!_usingLocalTestStorage && !_serverReady)
            || _loadResult?.Status == SkillProfileLoadStatus.MigrationRequired;
        _migrationButton.Visible = MigrationAvailable;
        _migrationButton.Disabled = _serverBusy;
        RestorePendingFocus();
    }

    private void RestorePendingFocus()
    {
        if (string.IsNullOrEmpty(_pendingFocusKey)) return;
        string key = _pendingFocusKey;
        _pendingFocusKey = "";
        string[] parts = key.Split(':', 2);
        if (parts.Length != 2) return;
        Button? target = parts[0] switch
        {
            "character" => _characterButtons.GetValueOrDefault(parts[1]),
            "active" => _activeButtons.GetValueOrDefault(parts[1]),
            "support" => _supportButtons.GetValueOrDefault(parts[1]),
            _ => null
        };
        if (target is not null && !target.Disabled && target.IsInsideTree()) target.GrabFocus();
    }

    private void ShowTransient(string message, bool isError)
    {
        _statusLabel.Text = message;
        _statusLabel.Modulate = OperationUiStyle.StatusColor(isError);
        OperationUiStyle.ApplyPanel(
            GetNode<Panel>("UI/MainPanel/StatusPanel"),
            true,
            OperationUiStyle.StatusColor(isError));
    }

    private SquadSkillValidationResult ValidateAllocation()
    {
        StageDefinition? stage = StageSelectionState.SelectedStage;
        var allocation = new SquadSkillAllocation
        {
            StageId = stage?.Id ?? "", PlayerUnlockedPoints = _progress.UnlockedPoints,
            HasStageCap = stage?.SkillBudget?.HasStageCap ?? false, StageCap = stage?.SkillBudget?.StageCap ?? 0,
            TotalAllocatedPoints = _squadPresetsById.Values.Sum(preset => preset.AllocatedPoints)
        };
        foreach (CatProfile profile in _selectedProfiles) allocation.CatProfiles.Add(profile);
        foreach (CatProfile profile in _selectedProfiles)
            if (_squadPresetsById.TryGetValue(profile.CharacterId, out CatSkillPreset? preset)) allocation.Presets.Add(preset);
        return _validator.Validate(allocation, _progress, Catalog);
    }

    private bool HasActiveIssue()
    {
        CatSkillPreset? preset = CurrentPreset;
        if (preset is null) return true;
        return _validation.Issues.Any(issue => issue.CharacterId == _selectedCharacterId
            && (issue.Error is SquadSkillError.MissingSkill or SquadSkillError.SkillNotOwned
                or SquadSkillError.ActiveRoleMismatch or SquadSkillError.InvalidSkillCost or SquadSkillError.UnitSkillMismatch)
            && (string.IsNullOrEmpty(preset.ActiveSkillId) || issue.Message.Contains(preset.ActiveSkillId, StringComparison.Ordinal)));
    }

    private bool HasSupportIssue(string supportId) => _validation.Issues.Any(issue =>
        issue.CharacterId == _selectedCharacterId
        && issue.Error is SquadSkillError.MissingSkill or SquadSkillError.SkillNotOwned
            or SquadSkillError.SupportRoleMismatch or SquadSkillError.InvalidSkillCost
            or SquadSkillError.DuplicateSupport or SquadSkillError.MissingRequiredTag or SquadSkillError.ForbiddenTag
        && issue.Message.Contains(supportId, StringComparison.Ordinal));

    private string FirstRelevantMessage(SquadSkillValidationResult result)
    {
        SquadSkillValidationIssue? issue = result.Issues.FirstOrDefault(candidate => candidate.CharacterId == _selectedCharacterId)
            ?? result.Issues.FirstOrDefault(candidate => string.IsNullOrEmpty(candidate.CharacterId)) ?? result.Issues.FirstOrDefault();
        return issue?.Message ?? "이 고양이의 스킬 슬롯을 확인하세요.";
    }

    private async Task LoadFromServerAsync()
    {
        _serverBusy = true;
        _remoteLoadStatus = "Loading";
        Refresh();
        SkillProfileServerResult result = await _serverClient.LoadAsync(
            ServerBaseUrl(), PlayerId(), _serverCancellation.Token);
        if (!IsInsideTree()) return;
        _serverBusy = false;
        if (!result.IsSuccess || result.Payload is null)
        {
            _remoteLoadStatus = result.ErrorCode;
            _serverReady = false;
            Refresh();
            return;
        }

        ApplyServerPayload(result.Payload);
        _remoteLoadStatus = "Loaded";
        _serverReady = true;
        _serverDirty = false;
        Refresh();
    }

    private async Task SaveToServerAsync(
        Godot.Collections.Array<CatSkillPreset> presets,
        bool isMigration,
        bool startAfterSave)
    {
        _serverBusy = true;
        _lastSaveStatus = "Saving";
        Refresh();
        SkillProfileServerResult result = await _serverClient.SaveAsync(
            ServerBaseUrl(), PlayerId(), presets, _serverRevision, isMigration, _serverCancellation.Token);
        if (!IsInsideTree()) return;
        _serverBusy = false;
        if (!result.IsSuccess || result.Payload is null)
        {
            _lastSaveStatus = result.ErrorCode;
            _remoteLoadStatus = result.IsConflict ? "Conflict" : result.ErrorCode;
            if (result.IsConflict && result.Payload is not null)
                _serverRevision = result.Payload.Revision;
            Refresh();
            ShowTransient(result.IsConflict
                ? "수정 필요 // 서버 저장 충돌이 발생했습니다. 현재 편집은 유지했습니다. 다시 저장하기 전에 확인하세요."
                : $"수정 필요 // 서버 {result.ErrorCode}: {result.Message}", true);
            return;
        }

        ApplyServerPayload(result.Payload);
        _serverReady = true;
        _serverDirty = false;
        _remoteLoadStatus = isMigration ? "Migrated" : "Saved";
        _lastSaveStatus = "Saved";
        Refresh(isMigration
            ? "로컬 프리셋 이전 완료 // 서버 진행도는 유지하고 프리셋만 이전했습니다."
            : "프리셋 저장 완료 // 서버에 캐릭터 ID별 설정을 저장했습니다.");
        if (startAfterSave)
        {
            _validation = ValidateAllocation();
            if (_validation.IsValid) ApplyLoadoutAndStartBattle();
        }
    }

    private void ApplyServerPayload(SkillProfileServerPayload payload)
    {
        var progress = new PlayerSkillProgress
        {
            PlayerLevel = payload.PlayerLevel,
            UnlockedPoints = payload.UnlockedPoints,
            OwnedSkillIds = new Godot.Collections.Array<string>(payload.OwnedSkillIds)
        };
        var presets = new Godot.Collections.Array<CatSkillPreset>();
        foreach (SkillPresetServerPayload preset in payload.Presets)
        {
            presets.Add(new CatSkillPreset
            {
                CharacterId = preset.CharacterId,
                ActiveSkillId = preset.ActiveSkillId,
                SupportSkillIds = new Godot.Collections.Array<string>(preset.SupportSkillIds),
                AllocatedPoints = preset.AllocatedPoints,
                UpdatedAtUtc = preset.UpdatedAtUtc.ToString("O")
            });
        }
        ApplyProfile(progress, presets);
        _serverRevision = payload.Revision;
    }

    private bool CanEditServerProfile()
    {
        if (_usingLocalTestStorage || (_serverReady && !_serverBusy)) return true;
        ShowTransient("수정 필요 // 서버의 스킬 프리셋이 아직 준비되지 않았습니다.", true);
        return false;
    }

    private bool ServerReadyOrLocal() => _usingLocalTestStorage || _serverReady;

    private void MarkEdited(CatSkillPreset preset)
    {
        preset.UpdatedAtUtc = DateTimeOffset.UtcNow.ToString("O");
        if (!_usingLocalTestStorage) _serverDirty = true;
    }

    private static string ServerBaseUrl() => (string)ProjectSettings.GetSetting(
        "defense_game/server/base_url", "http://127.0.0.1:5080");

    private static string PlayerId() => (string)ProjectSettings.GetSetting(
        "defense_game/player/development_user_id", "local-development-user");

    private static CatSkillPreset ClonePreset(CatSkillPreset source) => new()
    {
        CharacterId = source.CharacterId,
        ActiveSkillId = source.ActiveSkillId,
        SupportSkillIds = new Godot.Collections.Array<string>(source.SupportSkillIds),
        AllocatedPoints = source.AllocatedPoints,
        UpdatedAtUtc = source.UpdatedAtUtc
    };

    private static void ClearChildren(Node parent)
    {
        foreach (Node child in parent.GetChildren())
        {
            parent.RemoveChild(child);
            child.QueueFree();
        }
    }

    private static string CompatibilityText(SkillDefinition skill)
    {
        if (skill.RequiredAllTags.Count > 0) return $"모두 필요: {KoreanUiText.Tags(skill.RequiredAllTags, " + ")}";
        if (skill.RequiredAnyTags.Count > 0) return $"하나 필요: {KoreanUiText.Tags(skill.RequiredAnyTags, " / ")}";
        if (skill.ForbiddenTags.Count > 0) return $"사용 불가: {KoreanUiText.Tags(skill.ForbiddenTags, " / ")}";
        return "태그 조건 없음";
    }

    public override void _ExitTree()
    {
        _serverCancellation.Cancel();
        _serverCancellation.Dispose();
    }
}
