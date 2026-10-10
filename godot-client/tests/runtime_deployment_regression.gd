extends SceneTree

var failures := 0
var elapsed_frames := 0

func _process(_delta):
    elapsed_frames += 1
    if elapsed_frames > 1800:
        push_error("Runtime deployment QA timed out")
        quit(1)
    return false

func check(ok, message):
    if not ok:
        failures += 1
        push_error(message)

func ticks(count):
    for i in range(count):
        await physics_frame

func wait_until(predicate: Callable, max_physics_frames: int) -> bool:
    for i in range(max_physics_frames):
        if predicate.call():
            return true
        await physics_frame
    return predicate.call()

func _initialize():
    run.call_deferred()

func run():
    change_scene_to_file("res://scenes/stage_one.tscn")
    await ticks(5)
    var stage = current_scene
    var grid = stage.get_node("DeploymentGrid")
    var wave_label = stage.get_node("HUD/TopPanel/WaveLabel")

    check(stage.SecondsUntilFirstWave > 9.0, "Countdown starts when the stage enters")
    check("FIRST WAVE" in wave_label.text, "HUD shows first wave countdown")
    await ticks(300)
    check(get_nodes_in_group("enemies").is_empty(), "First wave does not start before ten seconds")

    var wave_started = await wait_until(func(): return not get_nodes_in_group("enemies").is_empty(), 360)
    check(wave_started, "First wave starts without any deployed unit")
    check("WAVE 01" in wave_label.text, "HUD changes to active wave status")

    check(stage.SelectPlacementType(0), "Archer remains selectable after the wave starts")
    check(grid.SelectCell(Vector2i(4, 2)), "Archer can be deployed during combat")
    check(stage.RemainingArchers == 1 and get_nodes_in_group("towers").size() == 1, "Runtime archer deployment consumes one unit")

    check(stage.SelectPlacementType(1), "Warrior remains selectable after the wave starts")
    check(grid.SelectCell(Vector2i(1, 3)), "Warrior can be deployed during combat")
    check(get_nodes_in_group("warriors").size() == 1, "Runtime warrior is created")

    check(stage.SelectPlacementType(2), "Healer remains selectable after the wave starts")
    check(grid.SelectCell(Vector2i(5, 2)), "Healer can be deployed during combat")
    check(get_nodes_in_group("healers").size() == 1, "Runtime healer is created")
    var tower = get_nodes_in_group("towers")[0]
    tower.TakeDamage(8.0)
    var runtime_heal_applied = await wait_until(func(): return tower.CurrentHealth > 12.0, 90)
    check(runtime_heal_applied, "Healer deployed during combat activates immediately")
    check(grid.PlacementEnabled, "Deployment remains open while combat is active")

    stage.RestartStage()
    await ticks(5)
    stage = current_scene
    grid = stage.get_node("DeploymentGrid")
    grid.SelectCell(Vector2i(4, 2))
    grid.SelectCell(Vector2i(5, 2))
    stage.SelectPlacementType(1)
    grid.SelectCell(Vector2i(1, 3))
    stage.SelectPlacementType(2)
    grid.SelectCell(Vector2i(7, 2))
    check("ALL UNITS DEPLOYED" in stage.get_node("HUD/TopPanel/PlacementLabel").text, "HUD reports all units deployed")
    await ticks(300)
    check(get_nodes_in_group("enemies").is_empty(), "Early full deployment does not shorten countdown")

    print("RUNTIME DEPLOYMENT QA failures=", failures)
    quit(0 if failures == 0 else 1)
