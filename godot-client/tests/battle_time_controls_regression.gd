extends SceneTree

var failures := 0
var elapsed_frames := 0

func _process(_delta):
    elapsed_frames += 1
    if elapsed_frames > 1400:
        push_error("Battle time controls QA timed out")
        Engine.time_scale = 1.0
        paused = false
        quit(1)
    return false

func check(ok, message):
    if not ok:
        failures += 1
        push_error(message)

func ticks(count):
    for _i in range(count):
        await physics_frame

func _initialize():
    run.call_deferred()

func run():
    change_scene_to_file("res://scenes/stage_one.tscn")
    await ticks(5)
    var stage = current_scene
    var grid = stage.get_node("DeploymentGrid")
    var hud = stage.get_node("HUD")
    var controls = hud.get_node("BattleControls")
    var pause_button: Button = controls.get_node("PauseButton")
    var speed_button: Button = controls.get_node("SpeedButton")
    var first_wave_button: Button = controls.get_node("FirstWaveButton")

    check(not stage.IsBattlePaused and stage.BattleSpeed == 1 and is_equal_approx(Engine.time_scale, 1.0),
        "Battle starts unpaused at normal speed")
    check(pause_button.text == "일시정지" and speed_button.text == "1×" \
        and not first_wave_button.disabled,
        "HUD exposes pause, speed and first-wave controls")

    check(stage.SelectDeploymentCharacter("starter_archer_a"), "Archer can be selected for cooldown fixture")
    check(grid.SelectCell(Vector2i(4, 2)) and grid.SelectFacing(0), "Archer deploys for cooldown fixture")
    check(stage.SelectDeployedUnitAtCell(Vector2i(4, 2)) and stage.RetreatSelectedUnit(),
        "Retreat starts a redeployment timer before pause")
    var countdown_before_pause: float = stage.SecondsUntilFirstWave
    var points_before_pause: float = stage.DeploymentPoints
    var redeploy_before_pause: float = stage.GetRedeployRemaining("starter_archer_a")

    pause_button.emit_signal("pressed")
    check(stage.IsBattlePaused and paused and pause_button.text == "계속" \
        and first_wave_button.disabled,
        "Pause freezes the tree while HUD controls remain interactive")
    check(hud.AreAllDeploymentCardsDisabled() \
        and not stage.SelectDeploymentCharacter("starter_warrior_a") \
        and not grid.PlacementEnabled,
        "Pause blocks deployment interactions")
    await ticks(120)
    check(is_equal_approx(stage.SecondsUntilFirstWave, countdown_before_pause),
        "First-wave countdown does not advance while paused")
    check(is_equal_approx(stage.DeploymentPoints, points_before_pause),
        "Deployment points do not regenerate while paused")
    check(is_equal_approx(stage.GetRedeployRemaining("starter_archer_a"), redeploy_before_pause),
        "Redeployment cooldown does not advance while paused")
    check(get_nodes_in_group("enemies").is_empty(), "No enemy spawns while paused")

    speed_button.emit_signal("pressed")
    check(stage.BattleSpeed == 2 and speed_button.text == "2×" \
        and is_equal_approx(Engine.time_scale, 2.0),
        "Speed control can select double speed while paused")
    pause_button.emit_signal("pressed")
    check(not stage.IsBattlePaused and not paused, "Continue resumes the scene tree")
    await ticks(60)
    check(countdown_before_pause - stage.SecondsUntilFirstWave > 1.8,
        "Double speed advances countdown by about two seconds per real second")
    check(stage.DeploymentPoints - points_before_pause > 5.5,
        "Double speed applies to deployment-point regeneration")
    check(redeploy_before_pause - stage.GetRedeployRemaining("starter_archer_a") > 1.8,
        "Double speed applies to redeployment cooldown")

    first_wave_button.emit_signal("pressed")
    check(stage.CurrentWaveNumber == 1 and stage.SecondsUntilFirstWave == 0.0 \
        and not get_nodes_in_group("enemies").is_empty(),
        "Early start immediately begins the first wave once")
    check(first_wave_button.disabled and not stage.StartFirstWaveEarly(),
        "Early-start control disables after the first wave begins")

    var moving_enemy = get_nodes_in_group("enemies")[0]
    var enemy_progress_before_pause: float = moving_enemy.progress
    pause_button.emit_signal("pressed")
    await ticks(60)
    check(is_equal_approx(moving_enemy.progress, enemy_progress_before_pause),
        "Pause freezes active enemy movement")
    pause_button.emit_signal("pressed")
    await ticks(30)
    check(moving_enemy.progress - enemy_progress_before_pause > 1.5,
        "Double speed advances active enemy movement")

    await ticks(260)
    check(stage.CurrentWaveNumber == 2,
        "Canceled first-wave reservation cannot start an extra wave later")

    stage.RestartStage()
    await ticks(5)
    stage = current_scene
    check(not paused and not stage.IsBattlePaused and stage.BattleSpeed == 1 \
        and is_equal_approx(Engine.time_scale, 1.0),
        "Restart restores global pause and speed defaults")

    hud = stage.get_node("HUD")
    controls = hud.get_node("BattleControls")
    speed_button = controls.get_node("SpeedButton")
    pause_button = controls.get_node("PauseButton")
    speed_button.emit_signal("pressed")
    pause_button.emit_signal("pressed")
    stage.ReturnToStageList()
    await ticks(5)
    check(current_scene != null and current_scene.name == "StageSelect" \
        and not paused and is_equal_approx(Engine.time_scale, 1.0),
        "Stage-list return restores global pause and speed defaults")

    print("BATTLE TIME CONTROLS QA failures=", failures)
    Engine.time_scale = 1.0
    paused = false
    quit(0 if failures == 0 else 1)
