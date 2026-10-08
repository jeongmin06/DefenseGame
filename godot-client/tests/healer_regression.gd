extends SceneTree
var failures := 0
var elapsed_frames := 0
func _process(_delta):
    elapsed_frames += 1
    if elapsed_frames > 12000:
        push_error("QA timed out before completion")
        quit(1)
    return false
func check(ok,msg):
    if not ok:
        failures += 1
        push_error(msg)
func ticks(n):
    for i in range(n): await physics_frame
func wait_until(predicate: Callable, max_physics_frames := 120) -> bool:
    for i in range(max_physics_frames):
        if predicate.call(): return true
        await physics_frame
    return predicate.call()
func _initialize(): run.call_deferred()
func run():
    change_scene_to_file("res://scenes/stage_one.tscn")
    await ticks(5)
    var grid = current_scene.get_node("DeploymentGrid")
    current_scene.SelectPlacementType(2)
    check(not grid.SelectCell(Vector2i(6,2)), "Support rejects path")
    check(not grid.SelectCell(Vector2i(15,0)), "Support rejects blocked")
    grid.SelectCell(Vector2i(4,2))
    var h = get_nodes_in_group("healers")[0]
    grid.SelectCell(Vector2i(3,2))
    grid.SelectCell(Vector2i(5,2))
    var a = get_nodes_in_group("towers")[0]
    var b = get_nodes_in_group("towers")[1]
    var ah = a.get_node("UnitHealth")
    var bh = b.get_node("UnitHealth")
    var hh = h.get_node("UnitHealth")
    check(h.MaxHealth == 18 and h.CurrentHealth == 18, "Healer HP")
    check(get_nodes_in_group("allies").size() == 3, "Shared allies group")
    await ticks(90)
    check(get_nodes_in_group("enemies").is_empty(), "Three units do not start waves")
    check(h.FindHealingTarget() == null, "Full HP excluded")
    ah.TakeDamage(10.0)
    bh.TakeDamage(10.0)
    var expected = a if a.get_instance_id() < b.get_instance_id() else b
    check(h.FindHealingTarget() == expected, "Instance tie break")
    bh.Heal(5.0)
    h.SetBattleActive(true)
    await ticks(20)
    check(a.CurrentHealth == 10, "No early heal")
    var first_heal = await wait_until(func(): return is_equal_approx(a.CurrentHealth, 14.0), 90)
    check(first_heal and b.CurrentHealth == 15, "Single four point heal")
    check(current_scene.get_node("Projectiles").get_child_count() == 0, "No heal projectile")
    h.SetBattleActive(false)
    ah.Heal(1000.0)
    bh.Heal(1000.0)
    check(a.CurrentHealth == 20, "Healing capped")
    ah.TakeDamage(15.0)
    h.SetBattleActive(true)
    await ticks(25)
    a.global_position = Vector2(-1000,-1000)
    bh.TakeDamage(12.0)
    var retargeted = await wait_until(func(): return is_equal_approx(b.CurrentHealth, 12.0), 90)
    check(retargeted and a.CurrentHealth == 5, "Retarget at heal frame after range exit")
    h.SetBattleActive(false)
    bh.Heal(1000.0)
    hh.TakeDamage(10.0)
    check(h.FindHealingTarget() == h, "Self healing eligible")
    h.SetBattleActive(true)
    var self_healed = await wait_until(func(): return h.CurrentHealth > 8.0, 90)
    check(self_healed, "Self heal applied")
    h.SetBattleActive(false)
    hh.TakeDamage(1000.0)
    hh.Heal(1000.0)
    check(h.CurrentHealth == 0, "No resurrection")
    check(grid.CanPlace(Vector2i(4,2),2), "Healer death releases cell")
    await ticks(5)
    check(get_nodes_in_group("healers").is_empty(), "Healer removed")
    reload_current_scene()
    await ticks(5)
    grid = current_scene.get_node("DeploymentGrid")
    grid.SelectCell(Vector2i(4,2))
    grid.SelectCell(Vector2i(9,3))
    grid.SelectCell(Vector2i(6,2))
    await ticks(90)
    check(get_nodes_in_group("enemies").is_empty(), "Wait for final healer")
    check(grid.SelectedProfile == 2, "Auto support selection")
    grid.SelectCell(Vector2i(5,2))
    h = get_nodes_in_group("healers")[0]
    check(h.CurrentHealth == 18, "Restart health")
    check(not grid.SelectCell(Vector2i(7,7)), "Battle placement lock")
    for i in range(7200):
        if current_scene.get_node("HUD/ResultPanel").visible: break
        await physics_frame
    check(current_scene.get_node("HUD/ResultPanel").visible, "Battle finished")
    print("HEALER BATTLE: ",current_scene.get_node("HUD/ResultPanel/ResultDetail").text)
    a = get_nodes_in_group("towers")[0]
    a.get_node("UnitHealth").TakeDamage(5.0)
    var hp = a.CurrentHealth
    await ticks(120)
    check(a.CurrentHealth == hp, "No healing after result")
    check(current_scene.get_node("HUD/TopPanel/HealerButton").disabled, "Result disables healer button")
    print("HEALER QA failures=", failures)
    quit(0 if failures == 0 else 1)
