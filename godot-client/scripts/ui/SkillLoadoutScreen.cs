using DefenseGame.Client.Data;
using DefenseGame.Client.Skills;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

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
    public string LoadStatusCode => _loadResult?.StatusCode ?? "";
    public string SaveStatusCode => _lastSaveStatus;

    private readonly Dictionary<string, SkillDefinition> _skillsById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SkillDefinition> _supportById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CatProfile> _profilesById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CatSkillPreset> _squadPresetsById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Button> _characterButtons = new(StringComparer.Ordinal);
    private readonly List<CatProfile> _selectedProfiles = new();
    private readonly SquadSkillValidator _validator = new();
    private readonly SkillProfileStore _profileStore = new();

    private PlayerSkillProgress _progress = null!;
    private Godot.Collections.Array<CatSkillPreset> _storedPresets = new();
    private SkillProfileLoadResult? _loadResult;
    private SquadSkillValidationResult _validation = new();
    private string _selectedCharacterId = "";
    private string _lastSaveStatus = "";
    private bool _requiresRecoverySave;

    private HBoxContainer _characterList = null!;
    private HBoxContainer _activeList = null!;
    private VBoxContainer _candidateList = null!;
    private Label _activeLabel = null!;
    private Label _budgetLabel = null!;
    private Label _statusLabel = null!;
    private Button _saveButton = null!;
    private Button _startButton = null!;

    private CatSkillPreset? CurrentPreset => _squadPresetsById.GetValueOrDefault(_selectedCharacterId);

    public override void _Ready()
    {
        _characterList = GetNode<HBoxContainer>("UI/MainPanel/CharacterList");
        _activeList = GetNode<HBoxContainer>("UI/MainPanel/ActiveCard/ActiveList");
        _candidateList = GetNode<VBoxContainer>("UI/MainPanel/SupportScroll/CandidateList");
        _activeLabel = GetNode<Label>("UI/MainPanel/ActiveCard/ActiveLabel");
        _budgetLabel = GetNode<Label>("UI/MainPanel/BudgetLabel");
        _statusLabel = GetNode<Label>("UI/MainPanel/StatusLabel");
        _saveButton = GetNode<Button>("UI/MainPanel/Actions/SaveButton");
        _startButton = GetNode<Button>("UI/MainPanel/Actions/StartButton");
        GetNode<Button>("UI/MainPanel/Actions/BackButton").Pressed += ReturnToFormation;
        _saveButton.Pressed += () => SaveProfile();
        _startButton.Pressed += () => StartBattle();

        StageDefinition? stage = StageSelectionState.SelectedStage;
        GetNode<Label>("UI/StageLabel").Text = stage is null
            ? "선택된 스테이지 없음"
            : $"{stage.DisplayName}  //  {stage.Id}";

        LoadCatalog();
        LoadProfile();
        BuildCharacterButtons();
        _selectedCharacterId = _selectedProfiles.FirstOrDefault()?.CharacterId ?? "";
        Refresh();
        _characterButtons.GetValueOrDefault(_selectedCharacterId)?.GrabFocus();
        QueueRedraw();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("ui_cancel"))
        {
            GetViewport().SetInputAsHandled();
            ReturnToStageList();
        }
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(0, 0, 1280, 720), new Color("102a24"));
        DrawCircle(new Vector2(1130, 110), 190, new Color("6d3425"));
        DrawCircle(new Vector2(1130, 110), 145, new Color("d98e3d"));
        DrawLine(new Vector2(760, 0), new Vector2(1280, 520), new Color("2f5f43"), 90);
    }

    public bool SelectCharacter(string characterId)
    {
        if (!_profilesById.ContainsKey(characterId)) return false;
        _selectedCharacterId = characterId;
        Refresh();
        return true;
    }

    public bool TrySelectActive(string id)
    {
        CatSkillPreset? preset = CurrentPreset;
        if (preset is null || !_skillsById.TryGetValue(id, out SkillDefinition? skill)
            || skill.Role != SkillRole.Active || !_progress.OwnedSkillIds.Contains(id))
        {
            ShowTransient($"수정 필요 // 액티브 스킬 '{id}'을 선택할 수 없습니다.", true);
            return false;
        }
        preset.ActiveSkillId = id;
        Touch(preset);
        Refresh();
        return true;
    }

    public bool TryToggleSupport(string id)
    {
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
            Touch(preset);
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
        Touch(preset);
        Refresh($"{support.DisplayName} 스킬을 장착했습니다.");
        return true;
    }

    public bool RemoveSupportAt(int index)
    {
        CatSkillPreset? preset = CurrentPreset;
        if (preset is null || index < 0 || index >= preset.SupportSkillIds.Count) return false;
        preset.SupportSkillIds.RemoveAt(index);
        preset.AllocatedPoints = preset.SupportSkillIds.Count;
        Touch(preset);
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
        // Stage 3 keeps the existing single-loadout battle bridge. Per-character instance mapping is stage 4.
        string firstCharacterId = _selectedProfiles.First().CharacterId;
        StageSelectionState.SelectedLoadout = _validation.GetLoadout(firstCharacterId);
        StageSelectionState.SelectedCharacterLoadouts = new Godot.Collections.Array<CharacterSkillLoadout>(_validation.CharacterLoadouts);
        GetTree().ChangeSceneToFile("res://scenes/stage_one.tscn");
        return true;
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
        if (!string.IsNullOrWhiteSpace(StageSelectionState.SkillProfileStoragePathOverride))
            _profileStore.ConfigureStoragePath(StageSelectionState.SkillProfileStoragePathOverride);
        _loadResult = _profileStore.Load();
        _progress = _loadResult.Progress;
        _storedPresets = _loadResult.Presets;
        _requiresRecoverySave = _loadResult.RequiresExplicitOverwrite;
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
            if (_selectedProfiles.Contains(profile))
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
            var button = new Button { Name = $"Character_{characterId}", CustomMinimumSize = new Vector2(300, 44) };
            button.AddThemeFontSizeOverride("font_size", 16);
            button.AddThemeColorOverride("font_color", new Color("fff0c2"));
            button.AddThemeStyleboxOverride("normal", CardStyle("3b4938", "8f6d38"));
            button.AddThemeStyleboxOverride("hover", CardStyle("536344", "f1bb55"));
            button.AddThemeStyleboxOverride("focus", CardStyle("536344", "fff0c2"));
            button.Pressed += () => SelectCharacter(characterId);
            _characterList.AddChild(button);
            _characterButtons.Add(characterId, button);
        }
    }

    private void RebuildActiveButtons()
    {
        ClearChildren(_activeList);
        foreach (SkillDefinition skill in Catalog.Skills)
        {
            if (skill.Role != SkillRole.Active) continue;
            string id = skill.Id;
            var button = new Button
            {
                Name = $"Active_{id}", Text = CurrentPreset?.ActiveSkillId == id ? "선택됨" : "선택",
                CustomMinimumSize = new Vector2(130, 42), Disabled = !_progress.OwnedSkillIds.Contains(id),
                TooltipText = UiLocalization.Description(skill.Description)
            };
            button.AddThemeFontSizeOverride("font_size", 14);
            button.AddThemeColorOverride("font_color", new Color("fff0c2"));
            button.AddThemeStyleboxOverride("normal", CardStyle("6f3424", "d8943f"));
            button.AddThemeStyleboxOverride("hover", CardStyle("8d4429", "ffd166"));
            button.Pressed += () => TrySelectActive(id);
            _activeList.AddChild(button);
        }
    }

    private void RebuildSupportButtons()
    {
        ClearChildren(_candidateList);
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
                CustomMinimumSize = new Vector2(900, 64), Alignment = HorizontalAlignment.Left,
                TooltipText = description
            };
            button.AddThemeFontSizeOverride("font_size", 16);
            button.AddThemeColorOverride("font_color", new Color(repair ? "ff8e78" : "fff0c2"));
            button.AddThemeStyleboxOverride("normal", CardStyle("3b4938", repair ? "d45d4c" : "8f6d38"));
            button.AddThemeStyleboxOverride("hover", CardStyle("536344", "f1bb55"));
            string id = support.Id;
            button.Pressed += () => TryToggleSupport(id);
            _candidateList.AddChild(button);
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
                CustomMinimumSize = new Vector2(900, 44), Alignment = HorizontalAlignment.Left
            };
            invalid.AddThemeFontSizeOverride("font_size", 15);
            invalid.AddThemeColorOverride("font_color", new Color("ff8e78"));
            invalid.AddThemeStyleboxOverride("normal", CardStyle("4b2b28", "d45d4c"));
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
            button.Text = $"{(selected ? ">" : " ")} {profile.DisplayName}  //  {KoreanUiText.Role(profile.Unit?.Role ?? UnitRole.Ranged)} · {id}{(repair ? "  //  수정 필요" : "")}";
            button.Modulate = new Color(repair ? "ffb09e" : selected ? "ffffff" : "c9d8bc");
        }

        CatSkillPreset? preset = CurrentPreset;
        string activeId = preset?.ActiveSkillId ?? "";
        string activeName = _skillsById.TryGetValue(activeId, out SkillDefinition? active) ? active.DisplayName : activeId;
        bool activeRepair = HasActiveIssue();
        _activeLabel.Text = $"액티브  //  {(string.IsNullOrEmpty(activeName) ? "누락" : activeName)}\n{(active is null ? activeId : KoreanUiText.Tags(active.Tags, "  ·  "))}{(activeRepair ? "   //   수정 필요" : "")}";
        _activeLabel.TooltipText = active is null ? "" : UiLocalization.Description(active.Description);
        _activeLabel.Modulate = new Color(activeRepair ? "ff8e78" : "fff0c2");

        StageSkillBudget? budget = StageSelectionState.SelectedStage?.SkillBudget;
        string cap = budget is null || !budget.HasStageCap ? "없음" : budget.StageCap.ToString();
        bool budgetRepair = _validation.Issues.Any(issue => string.IsNullOrEmpty(issue.CharacterId));
        _budgetLabel.Text = $"포인트   사용 가능 {UsablePoints}   사용 {UsedPoints}   남음 {RemainingPoints}   //   스테이지 상한 {cap}{(budgetRepair ? "   //   수정 필요" : "")}";
        _budgetLabel.Modulate = new Color(budgetRepair ? "ff8e78" : "ffd66b");

        RebuildActiveButtons();
        RebuildSupportButtons();
        string validationMessage = _validation.IsValid ? "모든 고양이 준비 완료" : "수정 필요 // " + FirstRelevantMessage(_validation);
        string loadNote = _loadResult is null ? "" : $"프리셋 {_loadResult.StatusCode}";
        _statusLabel.Text = string.IsNullOrEmpty(transientMessage) ? $"{loadNote}   //   {validationMessage}" : transientMessage;
        _statusLabel.Modulate = new Color(_validation.IsValid && !transientMessage.StartsWith("수정 필요", StringComparison.Ordinal)
            ? "c9e7ae" : "ff8e78");
        _startButton.Disabled = StageSelectionState.SelectedStage is null || !_validation.IsValid;
        _saveButton.Disabled = _loadResult?.Status == SkillProfileLoadStatus.MigrationRequired;
    }

    private void ShowTransient(string message, bool isError)
    {
        _statusLabel.Text = message;
        _statusLabel.Modulate = new Color(isError ? "ff8e78" : "c9e7ae");
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

    private static void Touch(CatSkillPreset preset) => preset.UpdatedAtUtc = DateTimeOffset.UtcNow.ToString("O");

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

    private static StyleBoxFlat CardStyle(string background, string border) => new()
    {
        BgColor = new Color(background), BorderColor = new Color(border),
        BorderWidthLeft = 2, BorderWidthTop = 2, BorderWidthRight = 2, BorderWidthBottom = 2,
        CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6, CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
        ContentMarginLeft = 14
    };
}
