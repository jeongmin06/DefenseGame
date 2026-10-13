extends SceneTree

var failures := 0
var elapsed_frames := 0

func _process(_delta):
    elapsed_frames += 1
    if elapsed_frames > 800:
        push_error("Deployment facing QA timed out")
        quit(1)
    return false

func check(ok, message):
    if not ok:
        failures += 1
        push_error(message)

func ticks(count):
    for i in range(count):
        await process_frame

func wait_until(predicate: Callable, max_frames := 300) -> bool:
    for i in range(max_frames):
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
    var origin = Vector2i(4, 2)
    var initial_points = stage.DeploymentPoints

    check(grid.SelectCell(origin), "Valid tile begins direction selection")
    check(stage.IsChoosingDeploymentFacing and grid.IsChoosingFacing, "Stage and grid expose pending direction state")
    check(stage.PendingDeploymentCell == origin, "Pending cell is retained")
    check(get_nodes_in_group("towers").is_empty(), "Pending placement does not instantiate a unit")
    check(is_equal_approx(stage.DeploymentPoints, initial_points), "Pending placement does not spend points")

    grid.SetPreviewFacing(0)
    check(Vector2i(5, 2) in grid.PreviewRangeCells and Vector2i(7, 2) in grid.PreviewRangeCells,
        "Right preview uses canonical cells")
    grid.SetPreviewFacing(1)
    check(Vector2i(4, 3) in grid.PreviewRangeCells and Vector2i(4, 5) in grid.PreviewRangeCells,
        "Down preview rotates canonical cells")
    grid.SetPreviewFacing(2)
    check(Vector2i(3, 2) in grid.PreviewRangeCells and Vector2i(1, 2) in grid.PreviewRangeCells,
        "Left preview rotates canonical cells")
    grid.SetPreviewFacing(3)
    check(Vector2i(4, 1) in grid.PreviewRangeCells,
        "Up preview rotates canonical cells and clips outside the map")

    check(grid.CancelFacingSelection(), "Pending direction can be canceled")
    check(not stage.IsChoosingDeploymentFacing and get_nodes_in_group("towers").is_empty(),
        "Cancel leaves the battlefield unchanged")
    check(grid.CanPlace(origin, 0) and is_equal_approx(stage.DeploymentPoints, initial_points),
        "Cancel preserves occupancy and points")

    check(grid.SelectCell(origin), "Canceled placement can be retried")
    check(grid.SelectFacing(2), "Left direction confirms placement")
    var towers = get_nodes_in_group("towers")
    check(towers.size() == 1, "Direction confirmation creates the unit once")
    var tower = towers[0]
    check(tower.Facing == 2, "Confirmed direction reaches the runtime unit")
    check(tower.IsInAttackRange(Vector2i(3, 2)) and tower.IsInAttackRange(Vector2i(1, 2)),
        "Tower accepts cells in the rotated left range")
    check(not tower.IsInAttackRange(Vector2i(5, 2)), "Tower rejects cells behind its facing")
    check(not grid.CanPlace(origin, 0), "Confirmed placement occupies the tile")
    check(stage.DeploymentPoints < initial_points, "Confirmed placement spends points")

    var warrior_def = load("res://data/units/cat_warrior.tres")
    check(await wait_until(func(): return stage.DeploymentPoints >= warrior_def.DeploymentCost),
        "Warrior cost recovers")
    check(stage.SelectPlacementType(1), "Warrior can be selected")
    check(grid.SelectCell(Vector2i(6, 2)) and grid.SelectFacing(1), "Warrior deploys facing down")
    var warrior = get_nodes_in_group("warriors")[0]
    check(warrior.IsInAttackRange(Vector2i(6, 2)) and warrior.IsInAttackRange(Vector2i(6, 3)),
        "Warrior rotates its self and front cells down")
    check(not warrior.IsInAttackRange(Vector2i(7, 2)), "Warrior rejects its unrotated side cell")

    var healer_def = load("res://data/units/cat_healer.tres")
    check(await wait_until(func(): return stage.DeploymentPoints >= healer_def.DeploymentCost),
        "Healer cost recovers")
    check(stage.SelectPlacementType(2), "Healer can be selected")
    check(grid.SelectCell(Vector2i(8, 2)) and grid.SelectFacing(3), "Healer deploys facing up")
    var healer = get_nodes_in_group("healers")[0]
    var in_front = Node2D.new()
    var behind = Node2D.new()
    stage.add_child(in_front)
    stage.add_child(behind)
    in_front.global_position = grid.CellToGlobal(Vector2i(8, 1))
    behind.global_position = grid.CellToGlobal(Vector2i(9, 2))
    check(healer.IsInHealingRange(in_front), "Healer accepts a cell in its rotated support range")
    check(not healer.IsInHealingRange(behind), "Healer rejects a cell outside its facing")

    print("DEPLOYMENT FACING QA failures=", failures)
    quit(0 if failures == 0 else 1)
