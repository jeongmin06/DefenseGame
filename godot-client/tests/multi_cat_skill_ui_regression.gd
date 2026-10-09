extends SceneTree

var failures := 0
var root_path := "/tmp/defense-multi-cat-ui-%d-%d" % [OS.get_process_id(), Time.get_ticks_usec()]
var profile_path := root_path + "/skill_profile.json"

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

func document(active_a: String, supports_a: Array, allocated_a: int, supports_b: Array, allocated_b: int) -> String:
    return JSON.stringify({
        "schemaVersion": 1,
        "playerSkillProgress": {
            "playerLevel": 5,
            "unlockedPoints": 5,
            "ownedSkillIds": ["basic_arrow", "basic_slash", "basic_heal", "multiple_projectiles", "piercing_shot", "fire_infusion", "healing_amplification"]
        },
        "catSkillPresets": [
            {
                "characterId": "starter_archer_a",
                "activeSkillId": active_a,
                "supportSkillIds": supports_a,
                "allocatedPoints": allocated_a,
                "updatedAtUtc": "2026-10-08T00:00:00Z"
            },
            {
                "characterId": "starter_archer_b",
                "activeSkillId": "basic_arrow",
                "supportSkillIds": supports_b,
                "allocatedPoints": allocated_b,
                "updatedAtUtc": "2026-10-08T00:00:00Z"
            }
        ]
    }, "  ")

func _initialize(): run.call_deferred()

func run():
    check(DirAccess.make_dir_recursive_absolute(root_path) == OK, "Create isolated UI profile directory")
    var invalid_text = document("missing_active", ["missing_support", "multiple_projectiles"], 2,
        ["fire_infusion", "fire_infusion"], 2)
    write_text(profile_path, invalid_text)

    change_scene_to_file("res://scenes/stage_select.tscn")
    await ticks(5)
    current_scene.SetSkillProfileStoragePathOverride(profile_path)
    current_scene.SetStageSquadStoragePathOverride(root_path + "/squad.json")
    current_scene.SelectStage(0)
    await ticks(5)
    current_scene.ContinueToSkills()
    await ticks(5)
    var screen = current_scene

    check(screen.LoadStatusCode == "Loaded", "Valid schema with content errors loads normally")
    check(screen.UsedPoints == 4 and screen.UsablePoints == 3 and screen.RemainingPoints == -1, "Over-budget restored allocation remains unchanged")
    check(screen.IsRepairRequired("starter_archer_a") and screen.IsRepairRequired("starter_archer_b"), "Character tabs show repair state")
    check("수정 필요" in screen.get_node("UI/MainPanel/ActiveCard/ActiveLabel").text, "Invalid active slot is marked")
    check(screen.get_node_or_null("UI/MainPanel/SupportScroll/CandidateList/InvalidSupport_0") != null, "Missing support slot is rendered and removable")
    check(not screen.StartBattle(), "Invalid restored allocation cannot enter battle")
    check(FileAccess.get_file_as_string(profile_path) == invalid_text, "Opening and validation do not rewrite invalid presets")

    check(screen.TrySelectActive("basic_arrow"), "User can explicitly repair the active slot")
    check(screen.RemoveSupportAt(0), "User can explicitly remove the missing support slot")
    check(screen.GetSupportIds("starter_archer_a") == PackedStringArray(["multiple_projectiles"]), "First cat repair preserves its remaining slot")
    check(screen.SelectCharacter("starter_archer_b"), "Second character is selected by stable ID")
    check(screen.get_node_or_null("UI/MainPanel/SupportScroll/CandidateList/InvalidSupport_1") != null, "Duplicate support slot is marked separately")
    check(screen.RemoveSupportAt(1), "User can remove only the duplicate slot")
    check(screen.GetSupportIds("starter_archer_b") == PackedStringArray(["fire_infusion"]), "Second cat keeps its independent support")
    check(screen.GetSupportIds("starter_archer_a") == PackedStringArray(["multiple_projectiles"]), "Second cat repair does not alter first cat")
    check(screen.UsedPoints == 2 and screen.RemainingPoints == 1, "Repair updates squad used and remaining points")
    check(not screen.IsRepairRequired("starter_archer_a") and not screen.IsRepairRequired("starter_archer_b"), "Repair indicators clear after valid edits")
    check(FileAccess.get_file_as_string(profile_path) == invalid_text, "Explicit edits still do not auto-save")
    check(screen.SaveProfile() and screen.SaveStatusCode == "Saved", "Explicit save persists repaired presets")
    check(FileAccess.get_file_as_string(profile_path) != invalid_text, "Save button is the write boundary")

    screen.ReturnToFormation()
    await ticks(5)
    current_scene.ReturnToStageList()
    await ticks(5)
    var recovery_path = root_path + "/recovery.json"
    write_text(recovery_path, "{ broken json")
    write_text(recovery_path + ".bak", document("basic_arrow", ["multiple_projectiles", "piercing_shot"], 2,
        ["fire_infusion"], 1))
    current_scene.SetSkillProfileStoragePathOverride(recovery_path)
    current_scene.SetStageSquadStoragePathOverride(root_path + "/recovery-squad.json")
    current_scene.SelectStage(0)
    await ticks(5)
    current_scene.ContinueToSkills()
    await ticks(5)
    screen = current_scene
    check(screen.LoadStatusCode == "RecoveredFromBackup", "Preparation reports backup recovery")
    check(not FileAccess.file_exists(recovery_path), "Corrupt primary is preserved away from the active path")
    check(screen.SaveProfile() and screen.SaveStatusCode == "Saved", "Explicit save uses SaveAfterRecovery")
    check(FileAccess.file_exists(recovery_path), "Recovered profile is published only after explicit save")

    print("MULTI CAT SKILL UI QA failures=", failures)
    quit(0 if failures == 0 else 1)
