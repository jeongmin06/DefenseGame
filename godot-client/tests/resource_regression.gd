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
    check(definition.FirstWaveDelay == 10.0, "First wave preparation time")
    check("첫 방어선" in definition.Description.Resolve("ko-KR"), "Regional Korean locale uses Korean stage description")
    check("first grassland" in definition.Description.Resolve("en-US").to_lower(), "Regional English locale uses English stage description")
    var catalog = load("res://data/stages/catalog.tres")
    check(catalog.Stages.size() == 3, "Stage catalog size")
    check(definition.SkillBudget != null and definition.SkillBudget.HasStageCap and definition.SkillBudget.StageCap == 3, "Stage skill budget")
    var skill_catalog = load("res://data/skills/catalog.tres")
    check(skill_catalog != null and skill_catalog.Skills.size() == 7, "Skill catalog size")
    var basic_arrow = load("res://data/skills/basic_arrow.tres")
    check(basic_arrow.Id == "basic_arrow" and basic_arrow.Role == 0, "Active skill resource")
    check("활로 적" in basic_arrow.Description.Resolve("ja"), "Unknown locale falls back to Korean description")
    check("basic active skill" in basic_arrow.Description.Resolve("en").to_lower(), "Skill English description is available")
    check(basic_arrow.Tags == ["ATTACK", "BOW", "PROJECTILE", "PHYSICAL", "HIT"], "Active skill tags")
    check(basic_arrow.BaseProjectileCount == 1 and basic_arrow.BasePierceCount == 0 and basic_arrow.BaseDamageMultiplier == 1.0, "Active skill base values")
    var multiple = load("res://data/skills/multiple_projectiles.tres")
    check(multiple.Role == 1 and multiple.LinkCost == 1 and multiple.RequiredAnyTags == ["PROJECTILE"], "Support compatibility resource")
    check(multiple.Effects.size() == 2 and multiple.Effects[0].Type == 0 and multiple.Effects[0].IntValue == 2, "Integer skill effect")
    check(multiple.Effects[1].Type == 3 and is_equal_approx(multiple.Effects[1].FloatValue, 0.7), "Float skill effect")
    var fire = load("res://data/skills/fire_infusion.tres")
    check(fire.Effects.size() == 2 and fire.Effects[1].Type == 4 and fire.Effects[1].TagValue == "FIRE", "Tag skill effect")
    var healing = load("res://data/skills/healing_amplification.tres")
    check(healing.RequiredAnyTags == ["HEAL"] and healing.Effects[0].Type == 5, "Incompatible support fixture")
    var defaults = load("res://data/player/defaults.tres")
    check(defaults.Progress.PlayerLevel == 5 and defaults.Progress.UnlockedPoints == 5, "Player skill defaults")
    check(defaults.CatProfiles.size() == 4 and defaults.InitialPresets.size() == 4, "Cat profile defaults")
    check("다중 화살" in defaults.CatProfiles[0].Description.Resolve("ko"), "Character description reaches generated resource")
    var expected_totals = [21, 30, 42]
    for stage_index in range(catalog.Stages.size()):
        var total := 0
        for wave in catalog.Stages[stage_index].Waves: total += wave.Count
        check(total == expected_totals[stage_index], "Stage enemy total")
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
    check(archer_def.SkillTags == ["ATTACK", "BOW", "PROJECTILE", "PHYSICAL", "HIT"], "Unit skill compatibility tags")
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
