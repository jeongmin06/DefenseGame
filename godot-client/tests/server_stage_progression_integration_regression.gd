extends SceneTree

const SERVER_URL := "http://127.0.0.1:5099"

var failures := 0
var elapsed_frames := 0

func _process(_delta):
    elapsed_frames += 1
    if elapsed_frames > 6000:
        push_error("Server stage progression integration QA timed out")
        quit(1)
    return false

func check(ok, message):
    if not ok:
        failures += 1
        push_error(message)

func wait_for(predicate: Callable, attempt_limit := 240) -> bool:
    for i in range(attempt_limit):
        if predicate.call(): return true
        await create_timer(0.05).timeout
    return predicate.call()

func configure_fast_stage(stage, should_win: bool):
    stage.BaseHealth = 5 if should_win else 1
    stage.FirstWaveDelay = 0.01
    stage.WaveGap = 0.01
    for wave in stage.Waves:
        wave.Count = 1
        wave.Interval = 0.01
        wave.HealthOverride = 1.0 if should_win else 9999.0
        wave.SpeedOverride = 3000.0

func enter_battle(should_win: bool) -> bool:
    var selector = current_scene
    configure_fast_stage(selector.Catalog.Stages[0], should_win)
    if not selector.SelectStage(0): return false
    var formation_ready := await wait_for(func(): return current_scene != null \
        and current_scene.name == "SquadFormation" and current_scene.IsServerReady)
    if not formation_ready or not current_scene.ContinueToSkills(): return false
    var skills_ready := await wait_for(func(): return current_scene != null \
        and current_scene.name == "SkillLoadout" and current_scene.IsServerReady)
    if not skills_ready or not current_scene.StartBattle(): return false
    return await wait_for(func(): return current_scene != null and current_scene.name == "StageOne")

func _initialize(): run.call_deferred()

func run():
    ProjectSettings.set_setting("defense_game/server/base_url", SERVER_URL)
    ProjectSettings.set_setting("defense_game/player/development_user_id",
        "godot-progress-%d-%d" % [OS.get_process_id(), Time.get_ticks_usec()])
    ProjectSettings.set_setting("defense_game/progress/submit_in_headless", true)

    change_scene_to_file("res://scenes/stage_select.tscn")
    var loaded := await wait_for(func(): return current_scene != null \
        and current_scene.name == "StageSelect" and current_scene.IsProgressReady)
    check(loaded, "New player progress loads from PlayerActor")
    if not loaded:
        quit(1)
        return
    check(not current_scene.IsStageLocked(0), "Stage one starts unlocked")
    check(current_scene.IsStageLocked(1), "Stage two starts locked")

    var battle_ready := await enter_battle(false)
    check(battle_ready, "Defeat fixture enters battle")
    if not battle_ready:
        quit(1)
        return
    var defeat_saved := await wait_for(func(): return current_scene.ResultSyncStatusCode == "Saved")
    check(defeat_saved, "Defeat result receives server confirmation")
    check("진행도 변경 없음" in current_scene.get_node("HUD/ResultPanel/ResultSyncLabel").text,
        "Defeat report explains that progress did not change")
    current_scene.ReturnToStageList()
    loaded = await wait_for(func(): return current_scene != null \
        and current_scene.name == "StageSelect" and current_scene.IsProgressReady)
    check(loaded and current_scene.IsStageLocked(1), "Defeat keeps stage two locked after reload")

    battle_ready = await enter_battle(true)
    check(battle_ready, "Victory fixture enters battle")
    if not battle_ready:
        quit(1)
        return
    var victory_saved := await wait_for(func(): return current_scene.ResultSyncStatusCode == "Saved", 400)
    check(victory_saved, "Victory result receives server confirmation")
    check("첫 클리어" in current_scene.get_node("HUD/ResultPanel/ResultSyncLabel").text,
        "Victory report explains first clear and unlock")
    current_scene.ReturnToStageList()
    loaded = await wait_for(func(): return current_scene != null \
        and current_scene.name == "StageSelect" and current_scene.IsProgressReady)
    check(loaded and "클리어" in current_scene.GetStageButtonText(0),
        "Reloaded selection marks stage one cleared")
    check(loaded and not current_scene.IsStageLocked(1) \
        and "도전 가능" in current_scene.GetStageButtonText(1),
        "Reloaded selection unlocks stage two")

    print("SERVER STAGE PROGRESSION INTEGRATION QA failures=", failures)
    quit(0 if failures == 0 else 1)
