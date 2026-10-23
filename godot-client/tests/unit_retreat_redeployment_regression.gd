extends SceneTree

var failures := 0
var elapsed_frames := 0

func _process(_delta):
    elapsed_frames += 1
    if elapsed_frames > 1800:
        push_error("Unit retreat and redeployment QA timed out")
        quit(1)
    return false

func check(ok, message):
    if not ok:
        failures += 1
        push_error(message)

func ticks(count):
    for _i in range(count):
        await physics_frame

func wait_until(predicate: Callable, max_frames := 600) -> bool:
    for _i in range(max_frames):
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
    var archer_def = load("res://data/units/cat_archer.tres")
    var warrior_def = load("res://data/units/cat_warrior.tres")
    var healer_def = load("res://data/units/cat_healer.tres")
    var mouse_def = load("res://data/units/mutant_mouse.tres")

    var archer_cell := Vector2i(4, 2)
    check(stage.SelectDeploymentCharacter("starter_archer_a"), "Archer can be selected")
    check(grid.SelectCell(archer_cell) and grid.SelectFacing(0), "Archer deploys before retreat")
    var first_archer = get_nodes_in_group("towers")[0]
    check(stage.SelectDeployedUnitAtCell(archer_cell), "Clicking an occupied cell selects its allied unit")
    check(hud.IsUnitSelectionVisible and hud.SelectedUnitCharacterId == "starter_archer_a",
        "Selected ally opens the retreat panel for the exact character")
    check(stage.SelectedDeploymentCharacterId.is_empty(),
        "Selecting a deployed ally clears the deployment-card selection")
    check("체력 20 / 20" in hud.SelectedUnitSummary and "+5" in hud.RetreatButtonText,
        "Retreat panel shows current health and data-defined refund")

    var points_before_retreat: float = stage.DeploymentPoints
    hud.get_node("UnitSelectionPanel/Content/RetreatButton").emit_signal("pressed")
    check(is_equal_approx(stage.DeploymentPoints, points_before_retreat + floori(
        archer_def.DeploymentCost * archer_def.RetreatRefundRate)),
        "Manual retreat refunds half the paid deployment cost exactly once")
    check(grid.CanPlace(archer_cell, 0), "Retreat releases the occupied tile immediately")
    check(stage.GetRedeployRemaining("starter_archer_a") > 4.9,
        "Retreat starts the character's data-defined redeployment cooldown")
    check(hud.IsDeploymentCardDisabled("starter_archer_a") \
        and "재배치 05초" in hud.GetDeploymentCardText("starter_archer_a"),
        "Retreat card shows a disabled second-based countdown")
    var points_after_retreat: float = stage.DeploymentPoints
    first_archer.TakeDamage(first_archer.MaxHealth + 1.0)
    check(not stage.RetreatSelectedUnit() and is_equal_approx(stage.DeploymentPoints, points_after_retreat),
        "Repeated retreat or late damage cannot duplicate the refund or cooldown")

    check(await wait_until(func(): return stage.GetRedeployRemaining("starter_archer_a") <= 0.0, 360),
        "Retreat cooldown finishes in simulation time")
    check(not hud.IsDeploymentCardDisabled("starter_archer_a") \
        and "배치 가능" in hud.GetDeploymentCardText("starter_archer_a"),
        "Cooldown completion restores the same character card")

    var redeploy_cell := Vector2i(5, 2)
    check(stage.SelectDeploymentCharacter("starter_archer_a"), "Returned archer card can be selected")
    check(grid.SelectCell(redeploy_cell) and grid.SelectFacing(2), "Returned archer can be redeployed")
    var redeployed_archer = get_nodes_in_group("towers")[0]
    check(redeployed_archer.CharacterId == "starter_archer_a" \
        and is_equal_approx(redeployed_archer.CurrentHealth, redeployed_archer.MaxHealth),
        "Redeployment preserves character identity and restores maximum health")

    check(await wait_until(func(): return stage.DeploymentPoints >= warrior_def.DeploymentCost),
        "Deployment points recover for the warrior")
    var warrior_cell := Vector2i(1, 3)
    check(stage.SelectDeploymentCharacter("starter_warrior_a"), "Warrior can be selected")
    check(grid.SelectCell(warrior_cell) and grid.SelectFacing(0), "Warrior deploys on the path")
    var warrior = get_nodes_in_group("warriors")[0]
    var blocked_enemy = mouse_def.Scene.instantiate()
    stage.get_node("EnemyPath").add_child(blocked_enemy)
    blocked_enemy.Setup(mouse_def, 100.0, 60.0)
    check(blocked_enemy.TryBlock(warrior) and blocked_enemy.IsBlocked(),
        "Warrior can own a real enemy block before defeat")
    var blocked_progress: float = blocked_enemy.progress
    var points_before_defeat: float = stage.DeploymentPoints
    warrior.TakeDamage(warrior.MaxHealth + 1.0)
    check(is_equal_approx(stage.DeploymentPoints, points_before_defeat),
        "Defeat starts redeployment without refunding points")
    check(stage.GetRedeployRemaining("starter_warrior_a") > 4.9 \
        and grid.CanPlace(warrior_cell, 1),
        "Defeated warrior enters cooldown and releases its path tile")
    await ticks(2)
    check(not blocked_enemy.IsBlocked() and blocked_enemy.progress > blocked_progress,
        "Defeated warrior releases a blocked enemy and movement resumes")
    blocked_enemy.queue_free()

    check(await wait_until(func(): return stage.DeploymentPoints >= healer_def.DeploymentCost),
        "Deployment points recover for the healer")
    var healer_cell := Vector2i(8, 3)
    check(stage.SelectDeploymentCharacter("starter_healer_a"), "Healer can be selected")
    check(grid.SelectCell(healer_cell) and grid.SelectFacing(3), "Healer deploys on grass")
    check(stage.SelectDeployedUnitAtCell(healer_cell) \
        and hud.SelectedUnitCharacterId == "starter_healer_a",
        "Healer uses the common occupied-cell selection flow")
    check(stage.RetreatSelectedUnit(), "Healer uses the common retreat flow")
    check(stage.GetRedeployRemaining("starter_healer_a") > 4.9 \
        and grid.CanPlace(healer_cell, 2),
        "Healer retreat starts cooldown and releases its tile")

    check(await wait_until(func(): return stage.GetRedeployRemaining("starter_warrior_a") <= 0.0, 360),
        "Defeated warrior card returns after cooldown")
    check(not hud.IsDeploymentCardDisabled("starter_warrior_a"),
        "Defeated character becomes deployable again")

    check(await wait_until(func(): return stage.DeploymentPoints >= warrior_def.DeploymentCost),
        "Deployment points recover for warrior retreat verification")
    check(stage.SelectDeploymentCharacter("starter_warrior_a"), "Returned warrior can be selected")
    check(grid.SelectCell(warrior_cell) and grid.SelectFacing(0), "Returned warrior can be redeployed")
    warrior = get_nodes_in_group("warriors")[0]
    blocked_enemy = mouse_def.Scene.instantiate()
    stage.get_node("EnemyPath").add_child(blocked_enemy)
    blocked_enemy.Setup(mouse_def, 100.0, 60.0)
    check(blocked_enemy.TryBlock(warrior), "Redeployed warrior can block an enemy")
    blocked_progress = blocked_enemy.progress
    check(stage.SelectDeployedUnitAtCell(warrior_cell) and stage.RetreatSelectedUnit(),
        "Blocked warrior can retreat through the common selection flow")
    await ticks(2)
    check(not blocked_enemy.IsBlocked() and blocked_enemy.progress > blocked_progress,
        "Retreating warrior releases a blocked enemy and movement resumes")
    blocked_enemy.queue_free()

    stage.RestartStage()
    await ticks(5)
    stage = current_scene
    hud = stage.get_node("HUD")
    check(stage.GetRedeployRemaining("starter_archer_a") == 0.0 \
        and stage.GetRedeployRemaining("starter_warrior_a") == 0.0 \
        and stage.GetRedeployRemaining("starter_healer_a") == 0.0,
        "Restart clears every redeployment cooldown")
    check(not hud.IsDeploymentCardDisabled("starter_archer_a") \
        and not hud.IsDeploymentCardDisabled("starter_warrior_a") \
        and not hud.IsDeploymentCardDisabled("starter_healer_a"),
        "Restart restores the initial undeployed squad cards")

    print("UNIT RETREAT REDEPLOYMENT QA failures=", failures)
    quit(0 if failures == 0 else 1)
