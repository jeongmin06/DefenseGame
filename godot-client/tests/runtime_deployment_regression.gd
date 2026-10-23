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

    var scout_def = load("res://data/units/cat_archer_scout.tres")
    var warrior_def = load("res://data/units/cat_warrior.tres")
    var healer_def = load("res://data/units/cat_healer.tres")
    check(hud.BeginCharacterDrag("starter_warrior_a", grid.CellToGlobal(Vector2i(1, 3))),
        "Warrior can begin a drag on an enemy path tile")
    check(grid.DragPreviewRangeCells.size() == 2 \
        and Vector2i(1, 3) in grid.DragPreviewRangeCells \
        and Vector2i(2, 3) in grid.DragPreviewRangeCells,
        "Warrior drag preview includes its hovered and front cells")
    hud.CancelCharacterDrag()
    check(hud.BeginCharacterDrag("starter_healer_a", grid.CellToGlobal(Vector2i(5, 2))),
        "Healer can begin a drag on a ground tile")
    check(grid.DragPreviewRangeCells.size() == healer_def.AttackCellOffsets.size() \
        and Vector2i(5, 2) in grid.DragPreviewRangeCells,
        "Healer drag preview uses its support range including the origin")
    hud.CancelCharacterDrag()
    check(hud.BeginCharacterDrag("starter_archer_b", grid.CellToGlobal(Vector2i(14, 3))),
        "Archer can begin a drag near the map edge")
    check(grid.DragPreviewRangeCells.size() == 3,
        "Drag range preview clips offsets that extend outside the map")
    hud.CancelCharacterDrag()
    var invalid_cell := Vector2i(1, 3)
    var invalid_drag_position: Vector2 = grid.CellToGlobal(invalid_cell) + Vector2(18, 10)
    check(hud.BeginCharacterDrag("starter_archer_b", invalid_drag_position), "Available character card can begin a drag")
    check(stage.SelectedDeploymentCharacterId == "starter_archer_b" and hud.IsDraggingCharacter, "Drag selects the exact character")
    check(not hud.DragPlacementValid and "지형" in hud.DragPlacementStatus,
        "Ranged drag marks an enemy path tile as invalid")
    check(not grid.HasDragRangePreview and grid.DragPreviewRangeCells.is_empty(),
        "Invalid drag does not show an attack range preview")
    var invalid_tint: Color = hud.get_node("DragGhost").material.get_shader_parameter("tint_color")
    check(invalid_tint.r > invalid_tint.g, "Invalid drag ghost uses a red tint")
    check(hud.get_node("DragGhost").position.is_equal_approx(grid.CellToGlobal(invalid_cell) + scout_def.Visual.SpritePosition),
        "Invalid drag ghost still snaps to the hovered tile center")
    check(hud.CompleteCharacterDrag(invalid_drag_position), "HUD completes the invalid drag request")
    check(stage.RemainingArchers == 2 and not hud.IsDeploymentCardDisabled("starter_archer_b"), "Invalid drop preserves character inventory")
    check(stage.DeploymentPoints >= 29.9, "Invalid drop does not spend deployment points")
    var valid_cell := Vector2i(4, 2)
    var valid_drag_position: Vector2 = grid.CellToGlobal(valid_cell) + Vector2(18, 10)
    check(hud.BeginCharacterDrag("starter_archer_b", valid_drag_position), "Character card can be dragged again after rejection")
    check(hud.DragPlacementValid and hud.DragPlacementStatus == "배치 가능",
        "Ranged drag marks a grass tile as valid")
    check(grid.HasDragRangePreview, "Valid drag shows an attack range preview")
    check(grid.DragPreviewRangeCells.size() == scout_def.AttackCellOffsets.size(),
        "Drag preview uses the selected unit's data-defined range")
    check(Vector2i(5, 1) in grid.DragPreviewRangeCells and Vector2i(7, 2) in grid.DragPreviewRangeCells,
        "Drag preview uses the canonical right-facing offsets")
    var drag_motion := InputEventMouseMotion.new()
    drag_motion.global_position = invalid_drag_position
    hud._Input(drag_motion)
    check(not hud.DragPlacementValid and not grid.HasDragRangePreview,
        "Moving from a valid cell to an invalid cell clears the previous range")
    drag_motion.global_position = valid_drag_position
    hud._Input(drag_motion)
    check(hud.DragPlacementValid and grid.HasDragRangePreview,
        "Moving back to a valid cell restores the selected unit's range")
    hud.CancelCharacterDrag()
    check(not grid.HasDragRangePreview and grid.DragPreviewRangeCells.is_empty(),
        "Canceling a valid drag clears the range through the drag-ended signal")
    check(hud.BeginCharacterDrag("starter_archer_b", valid_drag_position),
        "Character card can begin a valid drag after cancellation")
    var valid_tint: Color = hud.get_node("DragGhost").material.get_shader_parameter("tint_color")
    check(valid_tint.g > valid_tint.r, "Valid drag ghost uses a teal tint")
    check(hud.get_node("DragGhost").position.is_equal_approx(grid.CellToGlobal(valid_cell) + scout_def.Visual.SpritePosition),
        "Valid drag ghost snaps to the tile center")
    check(hud.CompleteCharacterDrag(valid_drag_position), "HUD completes the valid drag request")
    check(stage.IsChoosingDeploymentFacing and stage.RemainingArchers == 2, "Valid drop waits for direction confirmation")
    check(not grid.HasDragRangePreview and not grid.PreviewRangeCells.is_empty(),
        "Dropping clears the drag range and transitions to facing preview")
    check(stage.DeploymentPoints >= 29.9, "Pending direction does not spend deployment points")
    check(grid.SelectFacing(0), "Confirm archer facing")
    check(stage.RemainingArchers == 1 and get_nodes_in_group("towers").size() == 1, "Runtime archer deployment consumes one unit")
    check(get_nodes_in_group("towers")[0].Facing == 0, "Archer keeps confirmed facing")
    check(stage.PlacedArcherCharacterIds == PackedStringArray(["starter_archer_b"]), "Drag deploys the exact character ID")
    check(hud.IsDeploymentCardDisabled("starter_archer_b"), "Placed character card is disabled")
    check(is_equal_approx(stage.DeploymentPoints, 30.0 - scout_def.DeploymentCost), "Successful archer deployment spends its data-defined cost once")

    check(stage.SelectPlacementType(1), "Warrior remains selectable after the wave starts")
    check(grid.SelectCell(Vector2i(1, 3)), "Warrior can be deployed during combat")
    check(grid.SelectFacing(0), "Confirm warrior facing")
    check(get_nodes_in_group("warriors").size() == 1, "Runtime warrior is created")
    check(get_nodes_in_group("warriors")[0].Facing == 0, "Warrior keeps confirmed facing")
    check(is_equal_approx(stage.DeploymentPoints, 30.0 - scout_def.DeploymentCost - warrior_def.DeploymentCost), "Warrior deployment spends its configured cost")

    check(not stage.SelectPlacementType(2), "Unaffordable healer cannot be selected")
    check(hud.IsDeploymentCardDisabled("starter_healer_a"), "Unaffordable character card is disabled")
    check(not hud.BeginCharacterDrag("starter_healer_a", grid.CellToGlobal(Vector2i(5, 2))),
        "Unaffordable character cannot begin dragging")
    check(not hud.get_node("DragGhost").visible, "Unaffordable drag does not show a character ghost")
    check("비용 11 · 포인트 부족" in hud.GetDeploymentCardText("starter_healer_a"), "Card explains the insufficient deployment points")
    var healer_affordable = await wait_until(func(): return stage.DeploymentPoints >= 11.0, 240)
    check(healer_affordable and not hud.IsDeploymentCardDisabled("starter_healer_a"), "Regeneration re-enables an affordable card")
    check(stage.SelectPlacementType(2), "Healer becomes selectable after deployment points recover")
    check(grid.SelectCell(Vector2i(5, 2)), "Healer can be deployed during combat")
    check(grid.SelectFacing(2), "Confirm healer facing")
    check(get_nodes_in_group("healers").size() == 1, "Runtime healer is created")
    check(get_nodes_in_group("healers")[0].Facing == 2, "Healer keeps confirmed facing")
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
