extends SceneTree

var failures := 0

func check(ok: bool, message: String) -> void:
    if not ok:
        failures += 1
        push_error(message)

func has_error(result, code: String) -> bool:
    for issue in result.Issues:
        if issue.ErrorCode == code:
            return true
    return false

func copy_preset(source):
    var preset = CatSkillPreset.new()
    preset.CharacterId = source.CharacterId
    preset.ActiveSkillId = source.ActiveSkillId
    preset.SupportSkillIds.assign(source.SupportSkillIds)
    preset.AllocatedPoints = source.AllocatedPoints
    preset.UpdatedAtUtc = source.UpdatedAtUtc
    return preset

func make_allocation(defaults, points := 5, has_cap := true, cap := 3):
    var allocation = SquadSkillAllocation.new()
    allocation.StageId = "stage_01"
    allocation.PlayerUnlockedPoints = points
    allocation.HasStageCap = has_cap
    allocation.StageCap = cap
    allocation.CatProfiles.assign(defaults.CatProfiles)
    allocation.Presets.assign(defaults.InitialPresets)
    allocation.TotalAllocatedPoints = 3
    return allocation

func make_support(id: String):
    var skill = SkillDefinition.new()
    skill.Id = id
    skill.Role = 1
    skill.LinkCost = 1
    skill.RequiredAnyTags.assign(["PROJECTILE"])
    return skill

func _initialize() -> void:
    run.call_deferred()

func run() -> void:
    var defaults = load("res://data/player/defaults.tres")
    var catalog = load("res://data/skills/catalog.tres")
    var validator = SquadSkillValidator.new()

    check(defaults.Progress.PlayerLevel == 5 and defaults.Progress.UnlockedPoints == 5, "Initial player progress")
    check(defaults.Progress.OwnedSkillIds.size() == 7, "Initial owned skills")
    check(defaults.CatProfiles.size() == 4 and defaults.InitialPresets.size() == 4, "Initial cats and presets")
    check(defaults.CatProfiles[0].CharacterId == "starter_archer_a", "Stable first character ID")
    check(defaults.CatProfiles[1].CharacterId == "starter_archer_b", "Stable second character ID")
    check(defaults.CatProfiles[0].UnitId == "cat_archer" and defaults.CatProfiles[1].UnitId == "cat_archer", "Character and unit IDs stay separate")
    check(defaults.CatProfiles[0].Unit == defaults.CatProfiles[1].Unit, "Both starter characters may share one unit definition")

    var expected_caps = [3, 4, 5]
    for index in range(3):
        var stage = load("res://data/stages/stage_0%d.tres" % (index + 1))
        check(stage.SkillBudget.StageId == stage.Id, "Stage budget owns matching stage ID")
        check(stage.SkillBudget.HasStageCap and stage.SkillBudget.StageCap == expected_caps[index], "Stage cap fixture")
    var budget = StageSkillBudget.new()
    budget.HasStageCap = false
    budget.StageCap = 0
    check(budget.GetUsablePoints(5) == 5, "Missing cap uses player points")
    budget.HasStageCap = true
    check(budget.GetUsablePoints(5) == 0, "Explicit zero cap is distinct from no cap")

    var allocation = make_allocation(defaults)
    check(allocation.UsablePoints == 3, "Allocation computes capped usable points")
    var before_a: Array[String] = []
    before_a.assign(defaults.InitialPresets[0].SupportSkillIds)
    var before_tags: Array[String] = []
    before_tags.assign(defaults.CatProfiles[0].Unit.SkillTags)
    var valid = validator.Validate(allocation, defaults.Progress, catalog)
    check(valid.IsValid and valid.UsablePoints == 3, "Initial squad allocation validates")
    check(valid.CharacterLoadouts.size() == 4, "Valid allocation creates one loadout per character")
    check(valid.CharacterLoadouts[0].CharacterId == "starter_archer_a" and valid.CharacterLoadouts[1].CharacterId == "starter_archer_b", "Loadouts use deterministic character order")
    check(valid.GetLoadout("starter_archer_a").TotalLinkCost == 2, "Loadout maps by character ID")
    check(defaults.InitialPresets[0].SupportSkillIds == before_a and defaults.CatProfiles[0].Unit.SkillTags == before_tags, "Validation does not mutate resources or presets")

    var reversed = make_allocation(defaults)
    reversed.CatProfiles.reverse()
    reversed.Presets.reverse()
    var reversed_result = validator.Validate(reversed, defaults.Progress, catalog)
    check(reversed_result.IsValid and reversed_result.CharacterLoadouts[0].CharacterId == "starter_archer_a", "Input order does not affect loadout order")

    var no_cap = make_allocation(defaults, 5, false, 0)
    check(validator.Validate(no_cap, defaults.Progress, catalog).UsablePoints == 5, "Validator preserves no-cap semantics")
    var zero_cap = make_allocation(defaults, 5, true, 0)
    var zero_result = validator.Validate(zero_cap, defaults.Progress, catalog)
    check(zero_result.UsablePoints == 0 and has_error(zero_result, "SquadBudgetExceeded"), "Validator preserves explicit zero cap")
    var over_budget = make_allocation(defaults, 5, true, 2)
    var over_result = validator.Validate(over_budget, defaults.Progress, catalog)
    check(has_error(over_result, "SquadBudgetExceeded") and over_result.CharacterLoadouts.is_empty(), "Squad cap rejects all output loadouts")

    var total_mismatch = make_allocation(defaults)
    total_mismatch.TotalAllocatedPoints = 2
    check(has_error(validator.Validate(total_mismatch, defaults.Progress, catalog), "TotalAllocatedPointMismatch"), "Squad total must match preset sum")
    var point_mismatch = make_allocation(defaults, 4)
    check(has_error(validator.Validate(point_mismatch, defaults.Progress, catalog), "PlayerPointMismatch"), "Allocation points must match progress")
    var negative_cap = make_allocation(defaults, 5, true, -1)
    check(has_error(validator.Validate(negative_cap, defaults.Progress, catalog), "InvalidStageCap"), "Negative stage cap is invalid")

    var duplicate_character = make_allocation(defaults)
    duplicate_character.CatProfiles.append(defaults.CatProfiles[0])
    check(has_error(validator.Validate(duplicate_character, defaults.Progress, catalog), "DuplicateCharacter"), "Duplicate character IDs are invalid")
    var missing_preset = make_allocation(defaults)
    missing_preset.Presets.remove_at(1)
    missing_preset.TotalAllocatedPoints = 2
    check(has_error(validator.Validate(missing_preset, defaults.Progress, catalog), "MissingPreset"), "Every character requires a preset")
    var missing_character = make_allocation(defaults)
    missing_character.CatProfiles.remove_at(1)
    check(has_error(validator.Validate(missing_character, defaults.Progress, catalog), "MissingCharacter"), "Every preset requires a character")
    var duplicate_preset = make_allocation(defaults)
    duplicate_preset.Presets.append(defaults.InitialPresets[0])
    check(has_error(validator.Validate(duplicate_preset, defaults.Progress, catalog), "DuplicatePreset"), "Duplicate presets are invalid")

    var missing_unit = make_allocation(defaults)
    var detached = CatProfile.new()
    detached.CharacterId = "starter_archer_a"
    detached.UnitId = "cat_archer"
    missing_unit.CatProfiles[0] = detached
    check(has_error(validator.Validate(missing_unit, defaults.Progress, catalog), "MissingUnit"), "Missing unit reference is invalid")

    var allocated_mismatch = make_allocation(defaults)
    allocated_mismatch.Presets[0] = copy_preset(defaults.InitialPresets[0])
    allocated_mismatch.Presets[0].AllocatedPoints = 1
    allocated_mismatch.TotalAllocatedPoints = 2
    check(has_error(validator.Validate(allocated_mismatch, defaults.Progress, catalog), "AllocatedPointMismatch"), "Allocated points must match support count")
    var duplicate_support = make_allocation(defaults)
    duplicate_support.Presets[0] = copy_preset(defaults.InitialPresets[0])
    duplicate_support.Presets[0].SupportSkillIds[1] = "multiple_projectiles"
    check(has_error(validator.Validate(duplicate_support, defaults.Progress, catalog), "DuplicateSupport"), "Duplicate support is invalid")
    var missing_skill = make_allocation(defaults)
    missing_skill.Presets[0] = copy_preset(defaults.InitialPresets[0])
    missing_skill.Presets[0].SupportSkillIds[0] = "missing_skill"
    check(has_error(validator.Validate(missing_skill, defaults.Progress, catalog), "MissingSkill"), "Missing skill reference is invalid")

    var unowned_progress = PlayerSkillProgress.new()
    unowned_progress.PlayerLevel = 5
    unowned_progress.UnlockedPoints = 5
    unowned_progress.OwnedSkillIds.assign(["basic_arrow", "piercing_shot", "fire_infusion", "healing_amplification"])
    check(has_error(validator.Validate(make_allocation(defaults), unowned_progress, catalog), "SkillNotOwned"), "Unowned skill is invalid")
    var wrong_active = make_allocation(defaults)
    wrong_active.Presets[0] = copy_preset(defaults.InitialPresets[0])
    wrong_active.Presets[0].ActiveSkillId = "fire_infusion"
    check(has_error(validator.Validate(wrong_active, defaults.Progress, catalog), "ActiveRoleMismatch"), "Active slot role is validated")
    var wrong_support = make_allocation(defaults)
    wrong_support.Presets[0] = copy_preset(defaults.InitialPresets[0])
    wrong_support.Presets[0].SupportSkillIds[0] = "basic_arrow"
    check(has_error(validator.Validate(wrong_support, defaults.Progress, catalog), "SupportRoleMismatch"), "Support slot role is validated")
    var incompatible = make_allocation(defaults)
    incompatible.Presets[0] = copy_preset(defaults.InitialPresets[0])
    incompatible.Presets[0].SupportSkillIds[0] = "healing_amplification"
    check(has_error(validator.Validate(incompatible, defaults.Progress, catalog), "MissingRequiredTag"), "Support tag compatibility is validated")

    var expanded_catalog = SkillCatalog.new()
    expanded_catalog.Skills.assign(catalog.Skills)
    var expanded_progress = PlayerSkillProgress.new()
    expanded_progress.PlayerLevel = 10
    expanded_progress.UnlockedPoints = 10
    expanded_progress.OwnedSkillIds.assign(defaults.Progress.OwnedSkillIds)
    var incompatible_active = SkillDefinition.new()
    incompatible_active.Id = "heal_active"
    incompatible_active.Role = 0
    incompatible_active.LinkCost = 0
    incompatible_active.Tags.assign(["HEAL"])
    expanded_catalog.Skills.append(incompatible_active)
    expanded_progress.OwnedSkillIds.append(incompatible_active.Id)
    var unit_mismatch = make_allocation(defaults, 10, false, 0)
    unit_mismatch.Presets[0] = copy_preset(defaults.InitialPresets[0])
    unit_mismatch.Presets[0].ActiveSkillId = incompatible_active.Id
    check(has_error(validator.Validate(unit_mismatch, expanded_progress, expanded_catalog), "UnitSkillMismatch"), "Unit and active skill tags must be compatible")
    var five_supports = CatSkillPreset.new()
    five_supports.CharacterId = "starter_archer_a"
    five_supports.ActiveSkillId = "basic_arrow"
    for index in range(6):
        var extra = make_support("extra_support_%d" % index)
        expanded_catalog.Skills.append(extra)
        expanded_progress.OwnedSkillIds.append(extra.Id)
        if index < 5:
            five_supports.SupportSkillIds.append(extra.Id)
    five_supports.AllocatedPoints = 5
    var five_allocation = SquadSkillAllocation.new()
    five_allocation.PlayerUnlockedPoints = 10
    five_allocation.CatProfiles.append(defaults.CatProfiles[0])
    five_allocation.Presets.append(five_supports)
    five_allocation.TotalAllocatedPoints = 5
    var five_result = validator.Validate(five_allocation, expanded_progress, expanded_catalog)
    check(five_result.IsValid and five_result.GetLoadout("starter_archer_a").SupportSkills.size() == 5, "Squad validator safely supports five supports")
    five_supports.SupportSkillIds.append("extra_support_5")
    five_supports.AllocatedPoints = 6
    five_allocation.TotalAllocatedPoints = 6
    check(has_error(validator.Validate(five_allocation, expanded_progress, expanded_catalog), "TooManySupports"), "Six supports are rejected")

    var legacy_supports = []
    for index in range(4):
        legacy_supports.append(expanded_catalog.Skills[catalog.Skills.size() + 1 + index])
    var legacy = SkillLinkValidator.new().Validate(catalog.Skills[0], legacy_supports)
    check(not legacy.IsValid and legacy.ErrorCode == "TooManySupports", "Legacy link validator keeps the three-support default")

    print("SQUAD SKILL QA failures=", failures)
    quit(0 if failures == 0 else 1)
