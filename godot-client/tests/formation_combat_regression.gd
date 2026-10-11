extends SceneTree

var failures := 0
var root_path := "/tmp/defense-formation-combat-%d-%d" % [OS.get_process_id(), Time.get_ticks_usec()]

func check(ok, message):
    if not ok:
        failures += 1
        push_error(message)

func ticks(count):
    for i in range(count): await process_frame

func wait_until(predicate: Callable, max_frames := 300) -> bool:
    for i in range(max_frames):
        if predicate.call(): return true
        await physics_frame
    return predicate.call()

func enter_battle(character_ids: Array):
    var squad_path = root_path + "/squad-%d.json" % character_ids.size()
    var file = FileAccess.open(squad_path, FileAccess.WRITE)
    file.store_string(JSON.stringify({
        "schemaVersion": 1,
        "stageSquadPresets": [{"stageId": "stage_01", "characterIds": character_ids, "updatedAtUtc": "2026-10-08T00:00:00Z"}]
    }))
    file.close()
    change_scene_to_file("res://scenes/stage_select.tscn")
    await ticks(5)
    current_scene.SetStageSquadStoragePathOverride(squad_path)
    current_scene.SetSkillProfileStoragePathOverride(root_path + "/profile-%d.json" % character_ids.size())
    current_scene.SelectStage(0)
    await ticks(5)
    check(current_scene.ContinueToSkills(), "Formation continues for combat fixture")
    await ticks(5)
    check(current_scene.StartBattle(), "Skill preparation starts combat")
    await ticks(5)

func _initialize(): run.call_deferred()

func run():
    check(DirAccess.make_dir_recursive_absolute(root_path) == OK, "Create isolated combat directory")
    await enter_battle(["starter_archer_b", "starter_archer_a"])
    var stage = current_scene
    check(stage.RemainingArchers == 2 and stage.NextArcherCharacterId == "starter_archer_b", "Selected count and order reach combat")
    check(stage.ArcherAttackSettings.AddedHitDamage == 2.0, "First selected cat exposes its own composed settings")
    var grid = stage.get_node("DeploymentGrid")
    check(stage.SelectDeploymentCharacter("starter_archer_a"), "A character card can override formation order")
    check(grid.SelectCell(Vector2i(4, 2)), "Place explicitly selected archer")
    await ticks(2)
    check(stage.NextArcherCharacterId == "starter_archer_b", "Unplaced formation-order archer remains available")
    check(grid.SelectCell(Vector2i(5, 2)), "Place second selected archer")
    await ticks(2)
    var towers = get_nodes_in_group("towers")
    check(towers.size() == 2, "Two selected archers create two tower instances")
    check(towers[0].CharacterId == "starter_archer_a" and towers[1].CharacterId == "starter_archer_b", "Tower identity follows the explicitly selected cards")
    check(towers[0].get_node("RangedAttack").ProjectileCount == 3 and towers[0].get_node("RangedAttack").PierceCount == 1, "First tower receives character A projectile loadout")
    check(towers[1].get_node("RangedAttack").AddedHitDamage == 2.0, "Second tower receives character B fire loadout")

    stage.RestartStage()
    await ticks(5)
    check(current_scene.RemainingArchers == 2 and current_scene.NextArcherCharacterId == "starter_archer_b", "Retry preserves selected formation and order")

    current_scene.ReturnToStageList()
    await ticks(5)
    await enter_battle(["starter_archer_a", "starter_warrior_a", "starter_healer_a"])
    stage = current_scene
    check(stage.RemainingArchers == 1, "One-cat formation creates exactly one archer slot")
    grid = stage.get_node("DeploymentGrid")
    check(grid.SelectCell(Vector2i(4, 2)), "Place sole selected archer")
    check(stage.RemainingArchers == 0, "No legacy ranged count leaks into selected formation")
    check(await wait_until(func(): return stage.DeploymentPoints >= 12.0), "Warrior cost recovers during combat preparation")
    check(stage.SelectPlacementType(1), "Selected warrior remains deployable")
    check(grid.SelectCell(Vector2i(1, 3)), "Place selected warrior")
    check(stage.PlacedWarriorCharacterIds == PackedStringArray(["starter_warrior_a"]), "Warrior keeps its character ID")
    check(await wait_until(func(): return stage.DeploymentPoints >= 11.0), "Healer cost recovers after warrior deployment")
    check(stage.SelectPlacementType(2), "Selected healer remains deployable")
    check(grid.SelectCell(Vector2i(5, 3)), "Place selected healer")
    check(stage.PlacedHealerCharacterIds == PackedStringArray(["starter_healer_a"]), "Healer keeps its character ID")

    print("FORMATION COMBAT QA failures=", failures)
    quit(0 if failures == 0 else 1)
