using DefenseGame.Client.Data;
using DefenseGame.Client.Skills;
using Godot;
using System;
using System.Collections.Generic;

namespace DefenseGame.Client.UI;

public partial class SkillLoadoutScreen : Node2D
{
    private const string ActiveSkillId = "basic_arrow";

    [Export] public SkillCatalog Catalog { get; set; } = null!;

    public int CandidateCount => _supportById.Count;
    public int SelectedSupportCount => _selectedSupportIds.Count;
    public int UsedLinkCores { get; private set; }
    public int RemainingLinkCores => SkillLinkValidator.LinkCoreBudget - UsedLinkCores;
    public string StatusMessage => _statusLabel.Text;

    private readonly Dictionary<string, SkillDefinition> _supportById = new(StringComparer.Ordinal);
    private readonly HashSet<string> _selectedSupportIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Button> _buttonsById = new(StringComparer.Ordinal);
    private readonly SkillLinkValidator _validator = new();
    private SkillDefinition _activeSkill = null!;
    private VBoxContainer _candidateList = null!;
    private Label _activeLabel = null!;
    private Label _coreLabel = null!;
    private Label _statusLabel = null!;
    private Button _startButton = null!;

    public override void _Ready()
    {
        _candidateList = GetNode<VBoxContainer>("UI/MainPanel/CandidateList");
        _activeLabel = GetNode<Label>("UI/MainPanel/ActiveCard/ActiveLabel");
        _coreLabel = GetNode<Label>("UI/MainPanel/CoreLabel");
        _statusLabel = GetNode<Label>("UI/MainPanel/StatusLabel");
        _startButton = GetNode<Button>("UI/MainPanel/Actions/StartButton");
        GetNode<Button>("UI/MainPanel/Actions/BackButton").Pressed += ReturnToStageList;
        _startButton.Pressed += () => StartBattle();
        StageDefinition? stage = StageSelectionState.SelectedStage;
        GetNode<Label>("UI/StageLabel").Text = stage is null
            ? "NO STAGE SELECTED"
            : $"{stage.DisplayName.ToUpperInvariant()}  //  {stage.Id.Replace('_', ' ').ToUpperInvariant()}";

        LoadCatalog();
        RestoreSelection();
        BuildCandidateButtons();
        Refresh("Choose up to three compatible support skills.", false);
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

    public bool TryToggleSupport(string id)
    {
        if (!_supportById.TryGetValue(id, out SkillDefinition? support))
        {
            Refresh($"Unknown support skill '{id}'.", true);
            return false;
        }

        if (_selectedSupportIds.Contains(id))
        {
            var remaining = BuildSupportArray(id);
            SkillLinkValidationResult removal = _validator.Validate(_activeSkill, remaining);
            if (!removal.IsValid)
            {
                Refresh(removal.Message, true);
                return false;
            }
            _selectedSupportIds.Remove(id);
            ApplyValidResult(removal, $"Removed {support.DisplayName}.");
            return true;
        }

        var proposed = BuildSupportArray();
        proposed.Add(support);
        SkillLinkValidationResult addition = _validator.Validate(_activeSkill, proposed);
        if (!addition.IsValid)
        {
            Refresh(addition.Message, true);
            return false;
        }

        _selectedSupportIds.Add(id);
        ApplyValidResult(addition, $"Equipped {support.DisplayName}.");
        return true;
    }

    public bool IsSupportSelected(string id) => _selectedSupportIds.Contains(id);

    public bool StartBattle()
    {
        if (StageSelectionState.SelectedStage is null)
        {
            Refresh("Select a stage before entering battle.", true);
            return false;
        }

        SkillLinkValidationResult result = _validator.Validate(_activeSkill, BuildSupportArray());
        if (!result.IsValid || result.Loadout is null)
        {
            Refresh(result.Message, true);
            return false;
        }

        StageSelectionState.SelectedLoadout = result.Loadout;
        GetTree().ChangeSceneToFile("res://scenes/stage_one.tscn");
        return true;
    }

    public void ReturnToStageList()
    {
        StageSelectionState.SelectedStage = null;
        GetTree().ChangeSceneToFile("res://scenes/stage_select.tscn");
    }

    private void LoadCatalog()
    {
        foreach (SkillDefinition skill in Catalog.Skills)
        {
            if (skill.Id == ActiveSkillId)
                _activeSkill = skill;
            else if (skill.Role == SkillRole.Support)
                _supportById.Add(skill.Id, skill);
        }

        if (_activeSkill is null)
            throw new InvalidOperationException($"Skill catalog is missing '{ActiveSkillId}'.");

        _activeLabel.Text = $"ACTIVE  //  {_activeSkill.DisplayName.ToUpperInvariant()}\n{string.Join("  ·  ", _activeSkill.Tags)}";
    }

    private void RestoreSelection()
    {
        SkillLoadout? saved = StageSelectionState.SelectedLoadout;
        if (saved is null || saved.ActiveSkill.Id != ActiveSkillId) return;

        var candidates = new Godot.Collections.Array<SkillDefinition>();
        foreach (SkillDefinition skill in saved.SupportSkills)
            if (_supportById.TryGetValue(skill.Id, out SkillDefinition? current)) candidates.Add(current);

        SkillLinkValidationResult restored = _validator.Validate(_activeSkill, candidates);
        if (!restored.IsValid || restored.Loadout is null) return;
        foreach (SkillDefinition support in restored.Loadout.SupportSkills)
            _selectedSupportIds.Add(support.Id);
        UsedLinkCores = restored.Loadout.TotalLinkCost;
    }

    private void BuildCandidateButtons()
    {
        foreach (SkillDefinition support in Catalog.Skills)
        {
            if (support.Role != SkillRole.Support) continue;
            var button = new Button
            {
                Name = $"Support_{support.Id}",
                CustomMinimumSize = new Vector2(700, 62),
                Alignment = HorizontalAlignment.Left,
                TooltipText = CompatibilityText(support)
            };
            button.AddThemeFontSizeOverride("font_size", 18);
            button.AddThemeColorOverride("font_color", new Color("fff0c2"));
            button.AddThemeColorOverride("font_disabled_color", new Color("8f9b87"));
            button.AddThemeStyleboxOverride("normal", CardStyle("3b4938", "8f6d38"));
            button.AddThemeStyleboxOverride("hover", CardStyle("536344", "f1bb55"));
            button.AddThemeStyleboxOverride("focus", CardStyle("536344", "fff0c2"));
            string id = support.Id;
            button.Pressed += () => TryToggleSupport(id);
            _candidateList.AddChild(button);
            _buttonsById.Add(id, button);
        }

        if (_candidateList.GetChildCount() > 0)
            _candidateList.GetChild<Control>(0).GrabFocus();
    }

    private Godot.Collections.Array<SkillDefinition> BuildSupportArray(string excludedId = "")
    {
        var supports = new Godot.Collections.Array<SkillDefinition>();
        foreach (string id in _selectedSupportIds)
            if (id != excludedId && _supportById.TryGetValue(id, out SkillDefinition? skill)) supports.Add(skill);
        return supports;
    }

    private void ApplyValidResult(SkillLinkValidationResult result, string message)
    {
        UsedLinkCores = result.Loadout?.TotalLinkCost ?? 0;
        Refresh(message, false);
    }

    private void Refresh(string message, bool isError)
    {
        foreach ((string id, Button button) in _buttonsById)
        {
            SkillDefinition support = _supportById[id];
            bool selected = _selectedSupportIds.Contains(id);
            button.Text = $"{(selected ? "[X]" : "[ ]")}  {support.DisplayName.ToUpperInvariant()}   //   {CompatibilityText(support)}";
        }
        _coreLabel.Text = $"LINK CORES   USED {UsedLinkCores} / {SkillLinkValidator.LinkCoreBudget}    REMAINING {RemainingLinkCores}";
        _statusLabel.Text = message;
        _statusLabel.Modulate = new Color(isError ? "ff8e78" : "c9e7ae");
        _startButton.Disabled = StageSelectionState.SelectedStage is null;
    }

    private static string CompatibilityText(SkillDefinition skill)
    {
        if (skill.RequiredAllTags.Count > 0) return $"ALL: {string.Join(" + ", skill.RequiredAllTags)}";
        if (skill.RequiredAnyTags.Count > 0) return $"ANY: {string.Join(" / ", skill.RequiredAnyTags)}";
        if (skill.ForbiddenTags.Count > 0) return $"FORBIDS: {string.Join(" / ", skill.ForbiddenTags)}";
        return "NO TAG REQUIREMENT";
    }

    private static StyleBoxFlat CardStyle(string background, string border) => new()
    {
        BgColor = new Color(background),
        BorderColor = new Color(border),
        BorderWidthLeft = 2,
        BorderWidthTop = 2,
        BorderWidthRight = 2,
        BorderWidthBottom = 2,
        CornerRadiusTopLeft = 6,
        CornerRadiusTopRight = 6,
        CornerRadiusBottomLeft = 6,
        CornerRadiusBottomRight = 6,
        ContentMarginLeft = 18
    };
}
