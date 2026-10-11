extends SceneTree

var failures := 0
var elapsed_frames := 0

func _process(_delta):
    elapsed_frames += 1
    if elapsed_frames > 5000:
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
    var hud = stage.get_node("HUD")
    var wave_label = stage.get_node("HUD/TopPanel/WaveLabel")
    var deployment_points_label = stage.get_node("HUD/DeploymentPanel/DeploymentPointsLabel")

    check(hud.DeploymentCardCount == 4, "One deployment card is created per selected character")
    check("starter_archer_a" in hud.DeploymentCardCharacterIds, "Deployment cards preserve stable character IDs")
    check(stage.SecondsUntilFirstWave > 9.0, "Countdown starts when the stage enters")
    check("첫 웨이브" in wave_label.text, "HUD shows first wave countdown")
    await ticks(300)
    check(get_nodes_in_group("enemies").is_empty(), "First wave does not start before ten seconds")

    var wave_started = await wait_until(func(): return not get_nodes_in_group("enemies").is_empty(), 360)
    check(wave_started, "First wave starts without any deployed unit")
    check("웨이브 01" in wave_label.text, "HUD changes to active wave status")
    var points_reached_maximum = await wait_until(func(): return stage.DeploymentPoints >= 30.0, 10)
    check(points_reached_maximum, "Deployment points regenerate to the stage maximum")
    check("30 / 30" in deployment_points_label.text, "HUD shows current and maximum deployment points")

    check(hud.BeginCharacterDrag("starter_archer_b", Vector2(100, 600)), "Available character card can begin a drag")
    check(stage.SelectedDeploymentCharacterId == "starter_archer_b" and hud.IsDraggingCharacter, "Drag selects the exact character")
    check(hud.CompleteCharacterDrag(grid.CellToGlobal(Vector2i(1, 3))), "HUD completes the invalid drag request")
    check(stage.RemainingArchers == 2 and not hud.IsDeploymentCardDisabled("starter_archer_b"), "Invalid drop preserves character inventory")
    check(stage.DeploymentPoints >= 29.9, "Invalid drop does not spend deployment points")
    check(hud.BeginCharacterDrag("starter_archer_b", Vector2(100, 600)), "Character card can be dragged again after rejection")
    check(hud.CompleteCharacterDrag(grid.CellToGlobal(Vector2i(4, 2))), "HUD completes the valid drag request")
    check(stage.RemainingArchers == 1 and get_nodes_in_group("towers").size() == 1, "Runtime archer deployment consumes one unit")
    check(stage.PlacedArcherCharacterIds == PackedStringArray(["starter_archer_b"]), "Drag deploys the exact character ID")
    check(hud.IsDeploymentCardDisabled("starter_archer_b"), "Placed character card is disabled")
    check(stage.DeploymentPoints >= 19.9 and stage.DeploymentPoints < 20.1, "Successful archer deployment spends its cost once")

    check(stage.SelectPlacementType(1), "Warrior remains selectable after the wave starts")
    check(grid.SelectCell(Vector2i(1, 3)), "Warrior can be deployed during combat")
    check(get_nodes_in_group("warriors").size() == 1, "Runtime warrior is created")
    check(stage.DeploymentPoints >= 7.9 and stage.DeploymentPoints < 8.1, "Warrior deployment spends its configured cost")

    check(not stage.SelectPlacementType(2), "Unaffordable healer cannot be selected")
    check(hud.IsDeploymentCardDisabled("starter_healer_a"), "Unaffordable character card is disabled")
    check("비용 11 · 부족" in hud.GetDeploymentCardText("starter_healer_a"), "Card explains the insufficient deployment points")
    var healer_affordable = await wait_until(func(): return stage.DeploymentPoints >= 11.0, 240)
    check(healer_affordable and not hud.IsDeploymentCardDisabled("starter_healer_a"), "Regeneration re-enables an affordable card")
    check(stage.SelectPlacementType(2), "Healer becomes selectable after deployment points recover")
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
    check(stage.DeploymentPoints >= 20.0 and stage.DeploymentPoints < 20.2, "Restart restores the stage's initial deployment points")
    check(stage.RemainingArchers == 2, "Restart restores undeployed character inventory")

    print("RUNTIME DEPLOYMENT QA failures=", failures)
    quit(0 if failures == 0 else 1)
