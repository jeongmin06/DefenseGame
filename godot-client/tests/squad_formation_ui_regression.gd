extends SceneTree

var failures := 0
var root_path := "/tmp/defense-squad-formation-ui-%d-%d" % [OS.get_process_id(), Time.get_ticks_usec()]
var squad_path := root_path + "/squad_presets.json"

func check(ok, message):
    if not ok:
        failures += 1
        push_error(message)

func ticks(count):
    for i in range(count): await process_frame

func write_text(path: String, content: String):
    var file = FileAccess.open(path, FileAccess.WRITE)
    check(file != null, "Open fixture " + path)
    if file:
        file.store_string(content)
        file.flush()
        file.close()

func _initialize(): run.call_deferred()

func run():
    check(DirAccess.make_dir_recursive_absolute(root_path) == OK, "Create isolated formation directory")
    write_text(squad_path, JSON.stringify({
        "schemaVersion": 1,
        "stageSquadPresets": [{
            "stageId": "stage_02",
            "characterIds": ["starter_archer_b"],
            "updatedAtUtc": "2026-10-08T00:00:00Z"
        }]
    }, "  "))

    change_scene_to_file("res://scenes/stage_select.tscn")
    await ticks(5)
    current_scene.SetStageSquadStoragePathOverride(squad_path)
    current_scene.SetSkillProfileStoragePathOverride(root_path + "/skill_profile.json")
    current_scene.SelectStage(0)
    await ticks(5)
    var formation = current_scene
    check(formation.name == "SquadFormation", "Stage selection enters squad formation")
    check(formation.OwnedCharacterCount == 2, "Two owned cats are listed")
    check(formation.LegacyWarriorCount == 1 and formation.LegacyHealerCount == 1, "Legacy fixed roster counts are shown")
    check(formation.Capacity == 8 and formation.MaxSquadUnits == 10, "Selectable capacity excludes legacy units")
    check(formation.GetSelectedCharacterIds() == PackedStringArray(["starter_archer_a", "starter_archer_b"]), "New stage defaults to owned cats in stable order")
    check(FileAccess.get_file_as_string(squad_path).contains("stage_02"), "Opening formation does not write defaults")

    check(formation.MoveSelectedUp("starter_archer_b"), "Selected cat can move up")
    check(formation.GetSelectedCharacterIds() == PackedStringArray(["starter_archer_b", "starter_archer_a"]), "Deployment order changes explicitly")
    check(formation.ContinueToSkills(), "Valid formation continues to skills")
    await ticks(5)
    check(current_scene.name == "SkillLoadout", "Formation continues to skill preparation")
    var saved = JSON.parse_string(FileAccess.get_file_as_string(squad_path))
    check(saved.stageSquadPresets.size() == 2, "Saving one stage preserves other stage presets")
    check(saved.stageSquadPresets[0].stageId == "stage_02" and saved.stageSquadPresets[0].characterIds == ["starter_archer_b"], "Other stage preset remains unchanged")
    check(saved.stageSquadPresets[1].stageId == "stage_01" and saved.stageSquadPresets[1].characterIds == ["starter_archer_b", "starter_archer_a"], "Current stage order is saved")

    change_scene_to_file("res://scenes/squad_formation.tscn")
    await ticks(5)
    formation = current_scene
    check(formation.GetSelectedCharacterIds() == PackedStringArray(["starter_archer_b", "starter_archer_a"]), "Current session order survives skill-screen back navigation")
    check(formation.ToggleCharacter("starter_archer_b"), "A selected cat can be removed")
    check(formation.GetSelectedCharacterIds() == PackedStringArray(["starter_archer_a"]), "Formation supports a one-cat squad")

    formation.ReturnToStageList()
    await ticks(5)
    var invalid_path = root_path + "/invalid-content.json"
    var invalid_text = JSON.stringify({
        "schemaVersion": 1,
        "stageSquadPresets": [{
            "stageId": "stage_01",
            "characterIds": ["removed_cat", "removed_cat", "starter_archer_a", "starter_archer_b", "extra_1", "extra_2", "extra_3", "extra_4", "extra_5"],
            "updatedAtUtc": "2026-10-08T00:00:00Z"
        }]
    }, "  ")
    write_text(invalid_path, invalid_text)
    current_scene.SetStageSquadStoragePathOverride(invalid_path)
    current_scene.SelectStage(0)
    await ticks(5)
    formation = current_scene
    check(formation.LoadStatusCode == "Loaded", "Content-invalid squad is not file corruption")
    check(formation.GetSelectedCharacterIds().size() == 9, "Invalid restored selection remains unchanged for repair")
    check("수정 필요" in formation.StatusMessage, "Invalid restored selection shows repair state")
    check(not formation.ContinueToSkills(), "Invalid restored selection cannot continue")
    check(FileAccess.get_file_as_string(invalid_path) == invalid_text, "Validation never rewrites invalid restored content")

    print("SQUAD FORMATION UI QA failures=", failures)
    quit(0 if failures == 0 else 1)
