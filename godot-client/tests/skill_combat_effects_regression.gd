extends SceneTree

var failures := 0
var elapsed_frames := 0

func _process(_delta):
    elapsed_frames += 1
    if elapsed_frames > 900:
        push_error("Skill combat effects QA timed out")
        quit(1)
    return false

func check(ok, message):
    if not ok:
        failures += 1
        push_error(message)

func ticks(count):
    for i in range(count):
        await process_frame

func skill_by_id(catalog, id: String):
    for skill in catalog.Skills:
        if skill.Id == id:
            return skill
    return null

func validated_loadout(active, supports):
    var result = SkillLinkValidator.new().Validate(active, supports)
    check(result.IsValid, "Test loadout must validate")
    return result.Loadout

func spawn_enemy(mouse, position: Vector2, health: float):
    var enemy = mouse.Scene.instantiate()
    current_scene.get_node("EnemyPath").add_child(enemy)
    enemy.Setup(mouse, health, 1.0)
    enemy.SetBattleActive(false)
    enemy.global_position = position
    return enemy

func _initialize():
    run.call_deferred()

func run():
    var catalog = load("res://data/skills/catalog.tres")
    var active = skill_by_id(catalog, "basic_arrow")
    var multiple = skill_by_id(catalog, "multiple_projectiles")
    var piercing = skill_by_id(catalog, "piercing_shot")
    var fire = skill_by_id(catalog, "fire_infusion")
    var composer = RangedSkillEffectComposer.new()

    var defaults = composer.Compose(null)
    check(defaults.ProjectileCount == 1 and defaults.PierceCount == 0, "Null loadout keeps one non-piercing projectile")
    check(defaults.AddedHitDamage == 0.0 and defaults.DamageMultiplier == 1.0 and defaults.GetDamagePerHit(4.0) == 4.0, "Null loadout keeps base damage")

    var empty = composer.Compose(validated_loadout(active, []))
    check(empty.ProjectileCount == 1 and empty.PierceCount == 0 and empty.GetDamagePerHit(4.0) == 4.0, "Active-only loadout keeps base attack")
    check(empty.Tags == ["ATTACK", "BOW", "PROJECTILE", "PHYSICAL", "HIT"], "Active tags copied to runtime settings")

    var multiple_only = composer.Compose(validated_loadout(active, [multiple]))
    check(multiple_only.ProjectileCount == 3 and is_equal_approx(multiple_only.DamageMultiplier, 0.7), "Multiple projectiles values")
    check(is_equal_approx(multiple_only.GetDamagePerHit(4.0), 2.8), "Multiple projectiles damage multiplier")

    var piercing_only = composer.Compose(validated_loadout(active, [piercing]))
    check(piercing_only.ProjectileCount == 1 and piercing_only.PierceCount == 1, "Piercing values")

    var fire_only = composer.Compose(validated_loadout(active, [fire]))
    check(fire_only.AddedHitDamage == 2.0 and fire_only.Tags.has("FIRE"), "Fire damage and tag")
    check(fire_only.GetDamagePerHit(4.0) == 6.0, "Fire-only hit damage")

    var full_loadout = validated_loadout(active, [multiple, piercing, fire])
    var full = composer.Compose(full_loadout)
    check(full.ProjectileCount == 3 and full.PierceCount == 1, "All supports combine integer effects")
    check(full.AddedHitDamage == 2.0 and is_equal_approx(full.DamageMultiplier, 0.7), "All supports combine damage effects")
    check(is_equal_approx(full.GetDamagePerHit(4.0), 4.2) and full.Tags.has("FIRE"), "Full loadout damage and FIRE tag")
    check(active.BaseProjectileCount == 1 and active.BasePierceCount == 0 and active.BaseDamageMultiplier == 1.0, "Active resource remains unchanged")
    check(multiple.Effects[0].IntValue == 2 and fire.Effects[0].FloatValue == 2.0, "Support resources remain unchanged")

    change_scene_to_file("res://scenes/stage_select.tscn")
    await ticks(5)
    current_scene.SetSkillProfileStoragePathOverride("/tmp/defense-skill-combat-profile-%d-%d.json" % [OS.get_process_id(), Time.get_ticks_usec()])
    current_scene.SetStageSquadStoragePathOverride("/tmp/defense-skill-combat-squad-%d-%d.json" % [OS.get_process_id(), Time.get_ticks_usec()])
    current_scene.SelectStage(0)
    await ticks(5)
    current_scene.ContinueToSkills()
    await ticks(5)
    current_scene.SelectCharacter("starter_archer_b")
    current_scene.TryToggleSupport("fire_infusion")
    current_scene.SelectCharacter("starter_archer_a")
    current_scene.TryToggleSupport("fire_infusion")
    current_scene.StartBattle()
    await ticks(5)

    check(current_scene.Loadout != null and current_scene.Loadout.TotalLinkCost == 3, "Validated UI loadout reaches StageOne")
    var stage_settings = current_scene.ArcherAttackSettings
    check(stage_settings.ProjectileCount == 3 and stage_settings.PierceCount == 1, "StageOne composes loadout into ranged settings")
    check(is_equal_approx(stage_settings.GetDamagePerHit(4.0), 4.2) and stage_settings.Tags.has("FIRE"), "StageOne composed damage and tags")

    var grid = current_scene.get_node("DeploymentGrid")
    grid.SelectCell(Vector2i(4, 2))
    var tower = get_nodes_in_group("towers")[0]
    tower.SetBattleActive(false)
    var ranged = tower.get_node("RangedAttack")
    check(ranged.ProjectileCount == 3 and ranged.PierceCount == 1, "Tower receives shared ranged settings")
    check(ranged.AddedHitDamage == 2.0 and is_equal_approx(ranged.DamageMultiplier, 0.7) and ranged.AttackTags.has("FIRE"), "Ranged attack copies damage settings")

    var mouse = load("res://data/units/mutant_mouse.tres")
    var target = spawn_enemy(mouse, tower.global_position + Vector2(90, -28), 100.0)
    ranged.Fire(target, 4.0, false)
    var layer = current_scene.get_node("Projectiles")
    check(layer.get_child_count() == 3, "Multiple projectiles spawns three arrows")
    for projectile in layer.get_children():
        check(is_equal_approx(projectile.Damage, 4.2), "Each arrow receives final damage")
        check(projectile.RemainingPierces == 1 and projectile.HitTags.has("FIRE"), "Each arrow receives pierce and FIRE tag")
        projectile.queue_free()
    target.Despawn()
    await ticks(2)

    var fire_pierce = composer.Compose(validated_loadout(active, [piercing, fire]))
    ranged.ConfigureWithSettings(load("res://data/units/cat_archer.tres"), fire_pierce)
    var first = spawn_enemy(mouse, tower.global_position + Vector2(70, -28), 100.0)
    var second = spawn_enemy(mouse, tower.global_position + Vector2(130, -28), 100.0)
    ranged.Fire(first, 4.0, false)
    await ticks(30)
    check(is_equal_approx(first.CurrentHealth, 94.0), "First target takes fire-infused hit")
    check(is_equal_approx(second.CurrentHealth, 94.0), "Piercing arrow hits one additional enemy")
    check(layer.get_child_count() == 0, "Projectile is removed after its pierce budget is spent")

    print("SKILL COMBAT EFFECTS QA failures=", failures)
    quit(0 if failures == 0 else 1)
