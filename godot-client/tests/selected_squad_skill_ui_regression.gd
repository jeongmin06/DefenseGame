extends SceneTree

var failures := 0
var root_path := "/tmp/defense-selected-skill-ui-%d-%d" % [OS.get_process_id(), Time.get_ticks_usec()]
var squad_path := root_path + "/squad.json"
var profile_path := root_path + "/profile.json"

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
    check(DirAccess.make_dir_recursive_absolute(root_path) == OK, "Create isolated selected skill directory")
    write_text(squad_path, JSON.stringify({
        "schemaVersion": 1,
        "stageSquadPresets": [{"stageId": "stage_01", "characterIds": ["starter_archer_a"], "updatedAtUtc": "2026-10-08T00:00:00Z"}]
    }))
    write_text(profile_path, JSON.stringify({
        "schemaVersion": 1,
        "playerSkillProgress": {
            "playerLevel": 5, "unlockedPoints": 5,
            "ownedSkillIds": ["basic_arrow", "basic_slash", "basic_heal", "multiple_projectiles", "piercing_shot", "fire_infusion", "healing_amplification"]
        },
        "catSkillPresets": [
            {"characterId": "starter_archer_a", "activeSkillId": "basic_arrow", "supportSkillIds": ["multiple_projectiles", "piercing_shot"], "allocatedPoints": 2, "updatedAtUtc": "2026-10-08T00:00:00Z"},
            {"characterId": "starter_archer_b", "activeSkillId": "basic_arrow", "supportSkillIds": ["fire_infusion", "healing_amplification"], "allocatedPoints": 2, "updatedAtUtc": "2026-10-08T00:00:00Z"}
        ]
    }, "  "))

    change_scene_to_file("res://scenes/stage_select.tscn")
    await ticks(5)
    current_scene.SetStageSquadStoragePathOverride(squad_path)
    current_scene.SetSkillProfileStoragePathOverride(profile_path)
    current_scene.SelectStage(0)
    await ticks(5)
    check(current_scene.GetSelectedCharacterIds() == PackedStringArray(["starter_archer_a"]), "Stored one-cat formation loads")
    check(current_scene.ContinueToSkills(), "One-cat formation continues")
    await ticks(5)
    var screen = current_scene
    check(screen.CharacterCount == 1, "Only selected cat appears in skill preparation")
    check(screen.SelectedCharacterId == "starter_archer_a", "Selected character order drives initial skill tab")
    check(screen.UsedPoints == 2 and screen.UsablePoints == 3 and screen.RemainingPoints == 1, "Only selected cat consumes the stage budget")
    check(not screen.SelectCharacter("starter_archer_b"), "Unselected cat cannot be opened")
    check(screen.SaveProfile(), "Selected squad presets can be saved")
    var saved = JSON.parse_string(FileAccess.get_file_as_string(profile_path))
    check(saved.catSkillPresets.size() == 4, "All unselected character presets are preserved")
    check(saved.catSkillPresets[1].characterId == "starter_archer_b" and saved.catSkillPresets[1].supportSkillIds == ["fire_infusion", "healing_amplification"], "Unselected preset content remains unchanged")
    screen.ReturnToFormation()
    await ticks(5)
    check(current_scene.name == "SquadFormation", "Back returns to formation")
    check(current_scene.GetSelectedCharacterIds() == PackedStringArray(["starter_archer_a"]), "Back preserves current formation state")

    print("SELECTED SQUAD SKILL UI QA failures=", failures)
    quit(0 if failures == 0 else 1)
