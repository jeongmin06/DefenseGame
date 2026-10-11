extends SceneTree

var failures := 0
var profile_path := "/tmp/defense-skill-loadout-ui-%d-%d.json" % [OS.get_process_id(), Time.get_ticks_usec()]

func _process(_delta):
    if Engine.get_process_frames() > 900:
        push_error("Skill loadout UI QA timed out")
        quit(1)
    return false

func check(ok, message):
    if not ok:
        failures += 1
        push_error(message)

func ticks(count):
    for i in range(count): await process_frame

func _initialize(): run.call_deferred()

func run():
    change_scene_to_file("res://scenes/stage_select.tscn")
    await ticks(5)
    current_scene.SetSkillProfileStoragePathOverride(profile_path)
    current_scene.SetStageSquadStoragePathOverride("/tmp/defense-skill-loadout-squad-%d-%d.json" % [OS.get_process_id(), Time.get_ticks_usec()])
    current_scene.SelectStage(1)
    await ticks(5)
    check(current_scene.name == "SquadFormation", "Stage selection opens squad formation")
    current_scene.ContinueToSkills()
    await ticks(5)

    var screen = current_scene
    check(screen.name == "SkillLoadout", "Stage selection opens squad skill preparation")
    check(screen.CharacterCount == 4 and screen.SelectedCharacterId == "starter_archer_a", "Four character IDs and deterministic initial selection")
    check(screen.CandidateCount == 4, "All support candidates are displayed")
    check(screen.get_node("UI/MainPanel/CharacterList").get_child_count() == 4, "Four character buttons exist")
    check(screen.get_node("UI/MainPanel/SupportScroll/CandidateList").get_child_count() == 4, "Four normal support buttons exist")
    check(screen.LoadStatusCode == "Defaults", "Missing user file loads generated defaults")
    check(screen.GetSupportIds("starter_archer_a") == PackedStringArray(["multiple_projectiles", "piercing_shot"]), "First cat restores its own preset")
    check(screen.GetSupportIds("starter_archer_b") == PackedStringArray(["fire_infusion"]), "Second cat restores a separate preset")
    check(screen.UsablePoints == 4 and screen.UsedPoints == 3 and screen.RemainingPoints == 1, "Stage 02 displays squad point budget")
    check("스테이지 상한 4" in screen.get_node("UI/MainPanel/BudgetLabel").text, "Stage cap is visible in Korean")
    check("초급 궁수 A" in screen.get_node("UI/MainPanel/CharacterList/Character_starter_archer_a").text, "Localized character name is visible")
    check("다중 투사체" in screen.get_node("UI/MainPanel/SupportScroll/CandidateList/Support_multiple_projectiles").text, "Localized skill name is visible")
    check("화살을 두 발 추가" in screen.get_node("UI/MainPanel/SupportScroll/CandidateList/Support_multiple_projectiles").text, "Localized skill description is visible")
    check(screen.get_viewport().gui_get_focus_owner() != null, "Keyboard focus starts on a character")

    check(screen.SelectCharacter("starter_archer_b"), "Second cat can be selected by characterId")
    check(screen.SelectedSupportCount == 1 and screen.IsSupportSelected("fire_infusion"), "Second cat selection is displayed")
    check(screen.TryToggleSupport("fire_infusion"), "Second cat support can be removed")
    check(screen.UsedPoints == 2 and screen.RemainingPoints == 2, "Removing from one cat updates squad totals")
    check(screen.TryToggleSupport("multiple_projectiles"), "Point can be reassigned to second cat")
    check(screen.GetSupportIds("starter_archer_b") == PackedStringArray(["multiple_projectiles"]), "Second cat edit stays on second characterId")
    check(screen.GetSupportIds("starter_archer_a") == PackedStringArray(["multiple_projectiles", "piercing_shot"]), "First cat remains unchanged")
    check(not FileAccess.file_exists(profile_path), "Editing never auto-saves")

    check(not screen.TryToggleSupport("healing_amplification"), "Incompatible healing support is rejected")
    check("수정 필요" in screen.StatusMessage and "HEAL" in screen.StatusMessage, "Rejected slot shows repair reason")
    check(screen.SaveProfile() and screen.SaveStatusCode == "Saved", "Explicit save writes both character presets")
    check(FileAccess.file_exists(profile_path), "Explicit save creates the user profile")

    screen.ReturnToFormation()
    await ticks(5)
    current_scene.ContinueToSkills()
    await ticks(5)
    screen = current_scene
    check(screen.LoadStatusCode == "Loaded", "Reopening preparation loads the explicit save")
    check(screen.GetSupportIds("starter_archer_a") == PackedStringArray(["multiple_projectiles", "piercing_shot"]), "First cat persists independently")
    check(screen.GetSupportIds("starter_archer_b") == PackedStringArray(["multiple_projectiles"]), "Second cat persists independently")
    check(screen.StartBattle(), "Valid squad allocation enters battle")
    await ticks(5)
    check(current_scene.name == "StageOne" and current_scene.Definition.Id == "stage_02", "Selected stage reaches battle")
    check(current_scene.Loadout != null and current_scene.Loadout.TotalLinkCost == 2, "Stage 3 retains the legacy first-cat battle bridge")

    print("SKILL LOADOUT UI QA failures=", failures)
    quit(0 if failures == 0 else 1)
