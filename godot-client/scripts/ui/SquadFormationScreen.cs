using DefenseGame.Client.Data;
using DefenseGame.Client.Skills;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DefenseGame.Client.UI;

public partial class SquadFormationScreen : Node2D
{
    [Export] public PlayerSkillDefaults Defaults { get; set; } = null!;

    public int OwnedCharacterCount => Defaults?.CatProfiles.Count ?? 0;
    public int SelectedCount => _selectedIds.Count;
    public int LegacyWarriorCount { get; private set; }
    public int LegacyHealerCount { get; private set; }
    public int LegacyRosterCount => LegacyWarriorCount + LegacyHealerCount;
    public int Capacity { get; private set; }
    public int MaxSquadUnits => StageSelectionState.SelectedStage?.MaxSquadUnits ?? 0;
    public string StatusMessage => _statusLabel?.Text ?? "";
    public string LoadStatusCode => _loadResult?.StatusCode ?? "";
    public string SaveStatusCode => _lastSaveStatus;

    private readonly StageSquadPresetStore _store = new();
    private readonly StageSquadValidator _validator = new();
    private readonly Dictionary<string, CatProfile> _profilesById = new(StringComparer.Ordinal);
    private Godot.Collections.Array<StageSquadPreset> _presets = new();
    private readonly Godot.Collections.Array<string> _selectedIds = new();
    private StageSquadPresetLoadResult? _loadResult;
    private string _lastSaveStatus = "";
    private bool _requiresRecoverySave;
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
            ? "NO STAGE SELECTED"
            : $"{stage.DisplayName.ToUpperInvariant()}  //  {stage.Id.Replace('_', ' ').ToUpperInvariant()}";
        Load(stage);
        Refresh();
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
            _statusLabel.Text = $"수정 필요 // Select at most {Capacity} cats.";
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
            _statusLabel.Text = "수정 필요 // Select a stage first.";
            return false;
        }
        StageSquadValidationResult validation = Validate(stage);
        if (!validation.IsValid)
        {
            _statusLabel.Text = "수정 필요 // " + validation.Issues[0].Message;
            RefreshLists();
            return false;
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
        StageSelectionState.SelectedCharacterIds = new Godot.Collections.Array<string>(_selectedIds);
        StageSelectionState.SelectedProfiles = new Godot.Collections.Array<CatProfile>(validation.SelectedProfiles);
        StageSelectionState.ActiveSquadStageId = stage.Id;
        GetTree().ChangeSceneToFile("res://scenes/skill_loadout.tscn");
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
        if (!string.IsNullOrWhiteSpace(StageSelectionState.StageSquadStoragePathOverride))
            _store.ConfigureStoragePath(StageSelectionState.StageSquadStoragePathOverride);
        _loadResult = _store.Load();
        _presets = _loadResult.Presets;
        _requiresRecoverySave = _loadResult.RequiresExplicitOverwrite;
        if (stage is null) return;
        foreach (RosterEntry entry in stage.Roster)
        {
            if (entry.Unit.Role == UnitRole.Melee) LegacyWarriorCount += entry.Count;
            if (entry.Unit.Role == UnitRole.Support) LegacyHealerCount += entry.Count;
        }
        Capacity = Math.Max(0, stage.MaxSquadUnits - LegacyRosterCount);
        if (StageSelectionState.ActiveSquadStageId == stage.Id)
            _selectedIds.AddRange(StageSelectionState.SelectedCharacterIds);
        else
        {
            StageSquadPreset? stored = _presets.FirstOrDefault(item => item.StageId == stage.Id);
            if (stored is not null) _selectedIds.AddRange(stored.CharacterIds);
            else foreach (CatProfile profile in Defaults.CatProfiles.Take(Capacity)) _selectedIds.Add(profile.CharacterId);
        }
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
        LegacyRosterUnitCount = LegacyRosterCount,
        OwnedProfiles = Defaults.CatProfiles,
        CharacterIds = new Godot.Collections.Array<string>(_selectedIds)
    });

    private void Refresh()
    {
        StageDefinition? stage = StageSelectionState.SelectedStage;
        StageSquadValidationResult? validation = stage is null ? null : Validate(stage);
        _countsLabel.Text = $"SQUAD  {SelectedCount + LegacyRosterCount}/{MaxSquadUnits}   //   CATS {SelectedCount}/{Capacity}   //   WARRIOR {LegacyWarriorCount}   HEALER {LegacyHealerCount}";
        _continueButton.Disabled = validation is null || !validation.IsValid || _loadResult?.Status == StageSquadPresetLoadStatus.MigrationRequired;
        _statusLabel.Text = _loadResult?.Status == StageSquadPresetLoadStatus.MigrationRequired
            ? "수정 필요 // Saved squads require migration before they can be changed."
            : validation is { IsValid: false } ? "수정 필요 // " + validation.Issues[0].Message
            : _loadResult?.Status == StageSquadPresetLoadStatus.RecoveredFromBackup
                ? "수정 필요 // Restored the last valid backup. Review this formation before continuing."
            : _loadResult?.Status == StageSquadPresetLoadStatus.Corrupt
                ? "수정 필요 // Saved squad data was corrupt. Review this proposed formation before continuing."
            : "Formation ready. Order determines archer deployment order.";
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
                Text = $"{(selected ? "[X]" : "[ ]")}  {profile.DisplayName.ToUpperInvariant()}  //  {id}",
                CustomMinimumSize = new Vector2(450, 52), Alignment = HorizontalAlignment.Left
            };
            button.Pressed += () => ToggleCharacter(id);
            Style(button, selected ? "536344" : "3b4938", selected ? "ffd166" : "8f6d38");
            _ownedList.AddChild(button);
        }
        for (int index = 0; index < _selectedIds.Count; index++)
        {
            string id = _selectedIds[index];
            string label = _profilesById.TryGetValue(id, out CatProfile? profile) ? profile.DisplayName : "MISSING CHARACTER";
            var row = new HBoxContainer { Name = $"Selected_{index}" };
            var text = new Label { Text = $"{index + 1:00}  {label.ToUpperInvariant()}  //  {id}", CustomMinimumSize = new Vector2(330, 42) };
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
}
