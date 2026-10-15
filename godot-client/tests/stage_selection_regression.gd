extends SceneTree

var failures := 0
var elapsed_frames := 0

func _process(_delta):
    elapsed_frames += 1
    if elapsed_frames > 600:
        push_error("Stage selection QA timed out")
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
    var selector = current_scene
    selector.SetSkillProfileStoragePathOverride("/tmp/defense-stage-selection-profile-%d-%d.json" % [OS.get_process_id(), Time.get_ticks_usec()])
    selector.SetStageSquadStoragePathOverride("/tmp/defense-stage-selection-squad-%d-%d.json" % [OS.get_process_id(), Time.get_ticks_usec()])
    check(selector.StageCount == 3, "Three selectable stages")
    var background = selector.get_node("UI/Background")
    check(background.texture != null and background.texture.resource_path.ends_with("operation_preparation_ruined_city.png"),
        "Stage-select background shares the approved ruined-city operation art")
    check(selector.get_node("UI/ReadabilityOverlay").color.a >= 0.60,
        "Stage-select background has a readable operation overlay")
    var stage_list = selector.get_node("UI/StagePanel/StageList")
    check(stage_list.get_child_count() == 3, "Three generated buttons")
    var first_button = stage_list.get_child(0)
    for state in ["normal", "hover", "focus", "pressed", "disabled"]:
        check(first_button.get_theme_stylebox(state) is StyleBoxFlat,
            "Generated stage button has %s operation style" % state)
    check(first_button.custom_minimum_size.y == 112.0, "Operation button keeps the intended readable height")
    check("선택됨" in first_button.text, "Previewed stage writes its selected state")
    check(FileAccess.file_exists("res://assets/backgrounds/stage_select_command_outpost.png"),
        "Previous grassland-era stage background remains preserved")
    check(FileAccess.file_exists("res://assets/ui/stage_button_frame.png"),
        "Previous stage button art remains preserved")
    check(selector.GetEnemyCount(0) == 21, "Stage 1 enemy count")
    check(selector.GetEnemyCount(1) == 30, "Stage 2 enemy count")
    check(selector.GetEnemyCount(2) == 42, "Stage 3 enemy count")
    check("첫 방어선" in selector.get_node("UI/MapHint").text, "Korean stage description is visible")
    ProjectSettings.set_setting("defense_game/ui/locale", "en")
    selector.ShowStageDescription(0)
    check("first grassland" in selector.get_node("UI/MapHint").text.to_lower(), "English stage description is visible")
    ProjectSettings.set_setting("defense_game/ui/locale", "ko")
    selector.ShowStageDescription(0)

    selector.SelectStage(1)
    await ticks(5)
    check(current_scene.name == "SquadFormation", "Selection enters squad formation")
    check(current_scene.ContinueToSkills(), "Valid formation enters skill loadout")
    await ticks(5)
    check(current_scene.name == "SkillLoadout", "Formation enters skill loadout")
    check(current_scene.CandidateCount == 4, "Four support candidates")
    check(current_scene.StartBattle(), "Valid empty loadout enters battle")
    await ticks(5)
    check(current_scene.name == "StageOne", "Selection enters battle")
    check(current_scene.Definition.Id == "stage_02", "Selected definition reaches battle")
    check(current_scene.Loadout != null and current_scene.Loadout.TotalLinkCost == 2, "Validated first-cat loadout reaches battle")
    check(current_scene.RemainingArchers == 2, "Selected archer count reaches battle")
    check("초원 방어선 2" in current_scene.get_node("HUD/TopPanel/Title").text, "HUD shows localized selected stage")

    current_scene.RestartStage()
    await ticks(5)
    check(current_scene.Definition.Id == "stage_02", "Retry keeps selected stage")

    current_scene.ReturnToStageList()
    await ticks(5)
    check(current_scene.name == "StageSelect", "Result route returns to list")
    check(current_scene.StageCount == 3, "Catalog reloads after return")
    print("STAGE SELECT QA failures=", failures)
    quit(0 if failures == 0 else 1)
