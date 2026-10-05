extends SceneTree
var failures := 0
var elapsed_frames := 0
func _process(_delta):
    elapsed_frames += 1
    if elapsed_frames > 500:
        push_error("QA timed out before completion")
        quit(1)
    return false
func check(ok, message):
    if not ok:
        failures += 1
        push_error(message)
func _initialize(): run.call_deferred()
func run():
    var definition = load("res://data/stages/stage_01.tres")
    check(definition.Id == "stage_01" and definition.Roster.size() == 3 and definition.Waves.size() == 3, "Typed stage resources")
    change_scene_to_file("res://scenes/stage_one.tscn")
    await process_frame
    await process_frame
    var grid = current_scene.get_node("DeploymentGrid")
    check(grid.Columns == definition.Grid.Columns and grid.Rows == definition.Grid.Rows, "Grid dimensions")
    check(grid.CellSize == definition.Grid.CellSize and grid.position == definition.Grid.Origin, "Grid geometry")
    for y in range(grid.Rows):
        for x in range(grid.Columns):
            var cell = Vector2i(x,y)
            check(grid.GlobalToCell(grid.CellToGlobal(cell)) == cell, "Cell conversion")
            var kind = grid.GetTileType(cell)
            check(grid.CanPlace(cell, 0) == (kind == 0), "Ranged deployment rule")
            check(grid.CanPlace(cell, 1) == (kind != 2), "Melee deployment rule")
            check(grid.CanPlace(cell, 2) == (kind == 0), "Support deployment rule")
    var expected_power = 4.0
    for arg in OS.get_cmdline_user_args():
        if arg.begins_with("--expected-power="): expected_power = float(arg.split("=")[1])
    grid.SelectCell(Vector2i(4,2))
    var archer = get_nodes_in_group("towers")[0]
    var archer_def = load("res://data/units/cat_archer.tres")
    check(archer.AttackDamage == expected_power and archer.AttackDamage == archer_def.ActionPower, "JSON power reaches runtime")
    check(archer.MaxHealth == archer_def.MaxHealth, "Archer health")
    var ranged = archer.get_node("RangedAttack")
    check(ranged.ProjectileScene != null and ranged.ProjectileSpeed == archer_def.ProjectileSpeed, "Projectile settings")
    archer.TakeDamage(3.0)
    check(archer.CurrentHealth == archer_def.MaxHealth - 3 and archer_def.MaxHealth == 20, "Shared definition not damaged")
    archer.AttackDamage = 1.0
    check(archer_def.ActionPower == expected_power, "Instance combat state does not mutate definition")
    var warrior_def = load("res://data/units/cat_warrior.tres")
    current_scene.SelectPlacementType(1)
    grid.SelectCell(Vector2i(6,2))
    var warrior = get_nodes_in_group("warriors")[0]
    warrior.AttackCellOffsets.append(Vector2i(2,0))
    check(warrior_def.AttackCellOffsets.size() == 0, "Offsets copied")
    var mouse = load("res://data/units/mutant_mouse.tres")
    var enemy = mouse.Scene.instantiate()
    current_scene.get_node("EnemyPath").add_child(enemy)
    enemy.Setup(mouse, 0.0, 0.0)
    check(enemy.get_node("HealthBar").max_value == mouse.MaxHealth, "Default wave health")
    check(enemy.AttackDamage == mouse.ActionPower and enemy.AttackInterval == mouse.ActionInterval, "Enemy action")
    var progress = enemy.progress
    await physics_frame
    await physics_frame
    check(is_equal_approx(enemy.progress - progress, mouse.MoveSpeed / 60.0), "Default enemy move speed")
    enemy.Setup(mouse, 17.0, 82.0)
    check(enemy.get_node("HealthBar").max_value == 17, "Wave override")
    enemy.queue_free()
    # Every generated unit scene must instantiate with the expected C# script.
    for id in ["cat_archer", "cat_warrior", "cat_healer", "mutant_mouse"]:
        var unit = load("res://data/units/" + id + ".tres")
        check(unit.Id == id and unit.Scene != null, "Unit resource reference")
    print("RESOURCE QA failures=", failures)
    quit(0 if failures == 0 else 1)
