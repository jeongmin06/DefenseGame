extends SceneTree

var failures := 0
var elapsed_frames := 0

func _process(_delta):
    elapsed_frames += 1
    if elapsed_frames > 700:
        push_error("Skill loadout UI QA timed out")
        quit(1)
    return false

func check(ok, message):
    if not ok:
        failures += 1
        push_error(message)

func ticks(count):
    for i in range(count):
        await process_frame

func support_ids(loadout) -> Array[String]:
    var result: Array[String] = []
    for skill in loadout.SupportSkills:
        result.append(skill.Id)
    return result

func _initialize():
    run.call_deferred()

func run():
    change_scene_to_file("res://scenes/stage_select.tscn")
    await ticks(5)
    current_scene.SelectStage(1)
    await ticks(5)

    var screen = current_scene
    check(screen.name == "SkillLoadout", "Stage selection must open loadout UI")
    check(screen.CandidateCount == 4, "All four support candidates must be displayed")
    check(screen.get_node("UI/MainPanel/CandidateList").get_child_count() == 4, "Four candidate buttons must exist")
    check("BASIC ARROW" in screen.get_node("UI/MainPanel/ActiveCard/ActiveLabel").text, "Basic arrow must be the fixed active")
    check(screen.SelectedSupportCount == 0 and screen.UsedLinkCores == 0 and screen.RemainingLinkCores == 3, "Initial core state")
    check(screen.get_viewport().gui_get_focus_owner() != null, "Keyboard focus must start on a candidate")

    check(not screen.TryToggleSupport("healing_amplification"), "Incompatible healing support must be rejected")
    check("HEAL" in screen.StatusMessage and screen.SelectedSupportCount == 0, "Rejected support must show its reason without changing selection")

    check(screen.TryToggleSupport("multiple_projectiles"), "Multiple projectiles selection")
    check(screen.TryToggleSupport("piercing_shot"), "Piercing shot selection")
    check(screen.TryToggleSupport("fire_infusion"), "Fire infusion selection")
    check(screen.SelectedSupportCount == 3 and screen.UsedLinkCores == 3 and screen.RemainingLinkCores == 0, "Three supports consume all cores")
    check("[X]" in screen.get_node("UI/MainPanel/CandidateList/Support_fire_infusion").text, "Selected state must be visible")

    check(screen.StartBattle(), "Valid loadout must enter battle")
    await ticks(5)
    check(current_scene.name == "StageOne" and current_scene.Definition.Id == "stage_02", "Selected stage must reach battle")
    check(current_scene.Loadout != null and current_scene.Loadout.TotalLinkCost == 3, "Validated loadout must reach battle")
    check(support_ids(current_scene.Loadout) == ["fire_infusion", "multiple_projectiles", "piercing_shot"], "Battle loadout must use canonical order")

    current_scene.RestartStage()
    await ticks(5)
    check(current_scene.Loadout != null and current_scene.Loadout.TotalLinkCost == 3, "Retry must preserve loadout")

    current_scene.ReturnToStageList()
    await ticks(5)
    check(current_scene.name == "StageSelect", "Battle must return to stage list")
    current_scene.SelectStage(0)
    await ticks(5)
    screen = current_scene
    check(screen.name == "SkillLoadout" and screen.SelectedSupportCount == 3, "Returning through stage list must reopen the editable loadout")
    check(screen.TryToggleSupport("multiple_projectiles"), "Existing support can be removed")
    check(screen.SelectedSupportCount == 2 and screen.UsedLinkCores == 2 and screen.RemainingLinkCores == 1, "Edited core state")
    check(screen.StartBattle(), "Edited valid loadout must enter battle")
    await ticks(5)
    check(current_scene.Definition.Id == "stage_01", "New stage selection must reach battle")
    check(current_scene.Loadout.TotalLinkCost == 2 and support_ids(current_scene.Loadout) == ["fire_infusion", "piercing_shot"], "Edited loadout must replace the previous battle state")

    print("SKILL LOADOUT UI QA failures=", failures)
    quit(0 if failures == 0 else 1)
