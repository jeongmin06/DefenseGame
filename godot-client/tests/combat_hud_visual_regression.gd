extends SceneTree

var failures := 0
var elapsed_frames := 0

func _process(_delta):
    elapsed_frames += 1
    if elapsed_frames > 600:
        push_error("Combat HUD visual QA timed out")
        quit(1)
    return false

func check(ok, message):
    if not ok:
        failures += 1
        push_error(message)

func ticks(count):
    for i in range(count): await process_frame

func check_button_states(button: Button, label: String):
    for state in ["normal", "hover", "focus", "pressed", "disabled"]:
        check(button.get_theme_stylebox(state) is StyleBoxFlat, "%s has %s operation style" % [label, state])

func _initialize(): run.call_deferred()

func run():
    var transition := change_scene_to_file("res://scenes/stage_one.tscn")
    if transition != OK:
        check(false, "Combat scene starts")
        quit(1)
        return
    await ticks(5)
    if current_scene == null or current_scene.name != "StageOne":
        check(false, "Combat scene loads")
        quit(1)
        return

    var hud = current_scene.get_node("HUD")
    hud.UpdateStage("초원 방어선 1", "stage_01")
    check("stage_" not in hud.get_node("TopPanel/Title").text, "Combat header hides internal stage id")
    check(hud.get_node("TopPanel").get_theme_stylebox("panel") is StyleBoxFlat,
        "Top operation status uses the shared panel style")
    check(hud.get_node_or_null("TopPanel/PlacementCaption") != null,
        "Deployment instruction has a distinct caption")
    check(hud.get_node("TopPanel").size.y <= 90.0,
        "Compact top HUD leaves more battlefield visible")
    check(hud.get_node("DeploymentPanel").get_theme_stylebox("panel") is StyleBoxFlat,
        "Deployment deck uses the shared panel style")
    check(hud.get_node("DeploymentPanel/CardScroll") is ScrollContainer,
        "Deployment deck remains horizontally scrollable")
    check(hud.DeploymentCardCount == 4, "Selected squad creates four deployment cards")

    var first_id: String = hud.DeploymentCardCharacterIds[0]
    var first_card: Button = hud.get_node("DeploymentPanel/CardScroll/CardList/Card_" + first_id)
    check_button_states(first_card, "Deployment card")
    check("비용" in first_card.text, "Deployment card writes its deployment cost")
    check("starter_" not in first_card.text, "Deployment card hides internal character id")
    check(first_card.mouse_default_cursor_shape == Control.CURSOR_POINTING_HAND,
        "Deployment card uses pointing-hand cursor")
    check_button_states(hud.get_node("ResultPanel/RestartButton"), "Retry button")
    check_button_states(hud.get_node("ResultPanel/StageListButton"), "Stage-list button")
    var battle_controls: HBoxContainer = hud.get_node("BattleControls")
    var pause_button: Button = battle_controls.get_node("PauseButton")
    var speed_button: Button = battle_controls.get_node("SpeedButton")
    var first_wave_button: Button = battle_controls.get_node("FirstWaveButton")
    check_button_states(pause_button, "Pause button")
    check_button_states(speed_button, "Speed button")
    check_button_states(first_wave_button, "First-wave button")
    check(pause_button.text == "일시정지" and speed_button.text == "1×" \
        and not first_wave_button.disabled,
        "Battle time controls start in the expected Korean state")

    var card_scroll: ScrollContainer = hud.get_node("DeploymentPanel/CardScroll")
    check(card_scroll.horizontal_scroll_mode != ScrollContainer.SCROLL_MODE_DISABLED,
        "Deployment deck enables horizontal scrolling")
    check(first_card.custom_minimum_size.x * 10 > card_scroll.size.x,
        "The stage maximum of ten cards exceeds the deck viewport and uses scrolling")

    check(hud.BeginCharacterDrag(first_id, Vector2(300, 500)), "Deployment card starts a drag preview")
    await ticks(2)
    var drag_preview: PanelContainer = hud.get_node("DragPreview")
    var drag_ghost: AnimatedSprite2D = hud.get_node("DragGhost")
    check(drag_preview.visible and drag_preview.size.y >= 68.0,
        "Drag preview is visible and includes placement guidance")
    check(drag_ghost.visible and drag_ghost.sprite_frames != null,
        "Drag preview includes the selected character ghost")
    check(drag_ghost.sprite_frames.get_frame_texture("idle", 0) != null,
        "Character ghost uses the unit visual idle frame")
    check(drag_ghost.modulate.a >= 0.5 and drag_ghost.modulate.a <= 0.6,
        "Character ghost remains semi-transparent")
    var ghost_material := drag_ghost.material as ShaderMaterial
    check(ghost_material != null \
        and is_equal_approx(float(ghost_material.get_shader_parameter("opacity")), 0.55),
        "Chroma-key character ghost applies its opacity in the shader")
    var initial_ghost_position := drag_ghost.position
    var drag_motion := InputEventMouseMotion.new()
    drag_motion.global_position = Vector2(360, 460)
    hud._Input(drag_motion)
    await ticks(1)
    check(drag_ghost.position != initial_ghost_position,
        "Character ghost follows drag pointer movement")
    check(hud.CompleteCharacterDrag(Vector2(500, 300)), "Deployment card completes drag")
    check(not drag_preview.visible and not drag_ghost.visible,
        "Text preview and character ghost disappear after drop")
    check(hud.BeginCharacterDrag(first_id, Vector2(320, 480)), "Deployment card starts another drag")
    hud.CancelCharacterDrag()
    check(not drag_preview.visible and not drag_ghost.visible,
        "Text preview and character ghost disappear after cancellation")

    hud.ShowResult(true, 12, 1)
    check(hud.get_node("ResultPanel").visible, "Result report becomes visible")
    check(hud.get_node("ResultPanel/ResultTitle").text == "작전 성공", "Victory result remains Korean")
    check(hud.get_node("ResultPanel/ResultTitle").modulate.g > hud.get_node("ResultPanel/ResultTitle").modulate.r,
        "Victory result uses the teal success color")
    check(hud.ResultSyncStatus == "Saving", "Result report starts in server-saving state")
    check(pause_button.disabled and speed_button.disabled and first_wave_button.disabled,
        "Battle result disables every time control")
    check(hud.get_node("ResultPanel/RestartButton").disabled \
        and hud.get_node("ResultPanel/StageListButton").disabled,
        "Result navigation stays locked before server confirmation")
    hud.ShowResultSyncFailed("server_unavailable")
    check(hud.ResultSyncStatus == "server_unavailable" \
        and hud.get_node("ResultPanel/ResultSyncRetryButton").visible,
        "Failed result save exposes retry without enabling navigation")
    hud.ShowResultSyncSucceeded("서버 저장 완료 // 첫 클리어")
    check(hud.ResultSyncStatus == "Saved" \
        and not hud.get_node("ResultPanel/RestartButton").disabled \
        and not hud.get_node("ResultPanel/StageListButton").disabled,
        "Successful result save enables result navigation")

    print("COMBAT HUD VISUAL QA failures=", failures)
    quit(0 if failures == 0 else 1)
