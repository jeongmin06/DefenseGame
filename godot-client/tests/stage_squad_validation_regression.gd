extends SceneTree

var failures := 0

func check(ok, message):
    if not ok:
        failures += 1
        push_error(message)

func has_error(result, code: String) -> bool:
    for issue in result.Issues:
        if issue.ErrorCode == code:
            return true
    return false

func allocation(ids: Array[String], max_units := 10):
    var defaults = load("res://data/player/defaults.tres")
    var value = StageSquadAllocation.new()
    value.StageId = "stage_01"
    value.MaxSquadUnits = max_units
    value.OwnedProfiles.assign(defaults.CatProfiles)
    value.CharacterIds.assign(ids)
    return value

func _initialize():
    var validator = StageSquadValidator.new()
    var both = validator.Validate(allocation(["starter_archer_a", "starter_archer_b", "starter_warrior_a", "starter_healer_a"], 4))
    check(both.IsValid and both.SelectableCatCapacity == 4, "All four profiles fit the full squad capacity")
    check(both.SelectedProfiles[0].CharacterId == "starter_archer_a" and both.SelectedProfiles[1].CharacterId == "starter_archer_b", "Selection order is preserved")
    check(both.SelectedProfiles[0].UnitId == both.SelectedProfiles[1].UnitId, "Same unitId is allowed for different characterIds")
    check(validator.Validate(allocation(["starter_archer_b", "starter_archer_a"], 4)).SelectedProfiles[0].CharacterId == "starter_archer_b", "Reversed order remains meaningful")
    check(has_error(validator.Validate(allocation([], 4)), "EmptySquad"), "Empty squad is invalid")
    check(has_error(validator.Validate(allocation(["starter_archer_a", "starter_archer_a"], 4)), "DuplicateCharacter"), "Duplicate characterId is invalid")
    check(has_error(validator.Validate(allocation(["missing_cat"], 4)), "MissingCharacter"), "Unknown owned character is invalid")
    check(has_error(validator.Validate(allocation(["starter_archer_a", "starter_archer_b"], 1)), "CapacityExceeded"), "Selection cannot exceed the full squad capacity")
    check(has_error(validator.Validate(allocation(["starter_archer_a"], 0)), "InvalidMaxSquadUnits"), "Zero stage maximum is invalid")
    check(has_error(validator.Validate(allocation(["starter_archer_a"], 11)), "InvalidMaxSquadUnits"), "Maximum above ten is invalid")

    var defaults = load("res://data/player/defaults.tres")
    var missing_unit = allocation(["starter_archer_a"], 4)
    var detached = CatProfile.new()
    detached.CharacterId = "starter_archer_a"
    detached.UnitId = "cat_archer"
    missing_unit.OwnedProfiles[0] = detached
    check(has_error(validator.Validate(missing_unit), "MissingUnit"), "Missing unit reference is invalid")
    check(validator.Validate(allocation(["starter_warrior_a", "starter_healer_a"], 2)).IsValid, "Melee and support profiles are selectable")
    check(defaults.CatProfiles[0].CharacterId == "starter_archer_a", "Validation does not mutate defaults")
    for stage_id in ["stage_01", "stage_02", "stage_03"]:
        check(load("res://data/stages/" + stage_id + ".tres").MaxSquadUnits == 10, "Generated stage squad maximum")
    print("STAGE SQUAD VALIDATION QA failures=", failures)
    quit(0 if failures == 0 else 1)
