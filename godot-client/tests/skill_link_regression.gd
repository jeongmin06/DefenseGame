extends SceneTree

var failures := 0

func check(ok: bool, message: String) -> void:
    if not ok:
        failures += 1
        push_error(message)

func skill_by_id(catalog, id: String):
    for skill in catalog.Skills:
        if skill.Id == id:
            return skill
    return null

func make_support(id: String, any_tags := [], all_tags := [], forbidden_tags := [], link_cost := 1):
    var skill = SkillDefinition.new()
    skill.Id = id
    skill.Role = 1
    skill.LinkCost = link_cost
    var required_any: Array[String] = []
    var required_all: Array[String] = []
    var forbidden: Array[String] = []
    required_any.assign(any_tags)
    required_all.assign(all_tags)
    forbidden.assign(forbidden_tags)
    skill.RequiredAnyTags = required_any
    skill.RequiredAllTags = required_all
    skill.ForbiddenTags = forbidden
    return skill

func selected_ids(loadout) -> Array[String]:
    var result: Array[String] = []
    for skill in loadout.SelectedSkills:
        result.append(skill.Id)
    return result

func _initialize() -> void:
    run.call_deferred()

func run() -> void:
    var catalog = load("res://data/skills/catalog.tres")
    var active = skill_by_id(catalog, "basic_arrow")
    var multiple = skill_by_id(catalog, "multiple_projectiles")
    var piercing = skill_by_id(catalog, "piercing_shot")
    var fire = skill_by_id(catalog, "fire_infusion")
    var healing = skill_by_id(catalog, "healing_amplification")
    var validator = SkillLinkValidator.new()

    var valid = validator.Validate(active, [piercing, fire, multiple])
    check(valid.IsValid and valid.ErrorCode == "None", "Expected the three compatible supports to validate")
    check(valid.Loadout.TotalLinkCost == 3, "Expected all three link cores to be consumed")
    check(selected_ids(valid.Loadout) == ["basic_arrow", "fire_infusion", "multiple_projectiles", "piercing_shot"], "Loadout order must be canonical")

    var reversed = validator.Validate(active, [multiple, fire, piercing])
    check(reversed.IsValid and selected_ids(reversed.Loadout) == selected_ids(valid.Loadout), "Input order must not change loadout order")
    check(active.Tags == ["ATTACK", "BOW", "PROJECTILE", "PHYSICAL", "HIT"] and multiple.LinkCost == 1, "Validation must not mutate resources")

    var incompatible = validator.Validate(active, [healing])
    check(not incompatible.IsValid and incompatible.ErrorCode == "MissingAnyRequiredTag", "Healing support must be incompatible")
    check(incompatible.Message.contains("HEAL") and incompatible.Loadout == null, "Incompatibility must explain the missing HEAL tag")

    var duplicate = validator.Validate(active, [multiple, multiple])
    check(not duplicate.IsValid and duplicate.ErrorCode == "DuplicateSupport", "Duplicate support must be rejected")

    var wrong_role = validator.Validate(active, [active])
    check(not wrong_role.IsValid and wrong_role.ErrorCode == "SupportRoleRequired", "Active skill in a support slot must be rejected")
    var wrong_active = validator.Validate(healing, [])
    check(not wrong_active.IsValid and wrong_active.ErrorCode == "ActiveRoleRequired", "Support skill in the active slot must be rejected")
    var missing_active = validator.Validate(null, [])
    check(not missing_active.IsValid and missing_active.ErrorCode == "MissingActiveSkill", "Missing active skill must be rejected")

    var invalid_cost = make_support("invalid_cost", ["HIT"], [], [], 0)
    var bad_cost = validator.Validate(active, [invalid_cost])
    check(not bad_cost.IsValid and bad_cost.ErrorCode == "InvalidSupportLinkCost", "Invalid support link cost must be rejected")
    var expensive = make_support("expensive", ["HIT"], [], [], 2)
    var over_budget = validator.Validate(active, [expensive, multiple, piercing])
    check(not over_budget.IsValid and over_budget.ErrorCode == "LinkBudgetExceeded", "More than three link cores must be rejected")

    var fourth = make_support("fourth", ["HIT"])
    var too_many = validator.Validate(active, [multiple, piercing, fire, fourth])
    check(not too_many.IsValid and too_many.ErrorCode == "TooManySupports", "More than three supports must be rejected")

    var required_all_ok = make_support("all_ok", [], ["PROJECTILE", "HIT"])
    check(validator.Validate(active, [required_all_ok]).IsValid, "All required tags should match the active skill")
    var required_all_bad = make_support("all_bad", [], ["HIT", "HEAL"])
    var missing_all = validator.Validate(active, [required_all_bad])
    check(not missing_all.IsValid and missing_all.ErrorCode == "MissingAllRequiredTag" and missing_all.Message.contains("HEAL"), "Missing required-all tag must be reported")

    var forbidden = make_support("forbidden", [], [], ["BOW"])
    var forbidden_result = validator.Validate(active, [forbidden])
    check(not forbidden_result.IsValid and forbidden_result.ErrorCode == "ForbiddenTag" and forbidden_result.Message.contains("BOW"), "Forbidden active tag must be reported")

    var fire_chain = make_support("requires_fire", ["FIRE"])
    var no_chain_unlock = validator.Validate(active, [fire, fire_chain])
    check(not no_chain_unlock.IsValid and no_chain_unlock.ErrorCode == "MissingAnyRequiredTag", "Support-added FIRE tag must not unlock another support")

    print("SKILL LINK QA failures=", failures)
    quit(0 if failures == 0 else 1)
