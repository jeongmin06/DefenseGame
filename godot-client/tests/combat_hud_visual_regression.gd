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
    check(hud.get_node("TopPanel").get_theme_stylebox("panel") is StyleBoxFlat,
        "Top operation status uses the shared panel style")
    check(hud.get_node_or_null("TopPanel/PlacementCaption") != null,
        "Deployment instruction has a distinct caption")
    check(hud.get_node("DeploymentPanel").get_theme_stylebox("panel") is StyleBoxFlat,
        "Deployment deck uses the shared panel style")
    check(hud.get_node("DeploymentPanel/CardScroll") is ScrollContainer,
        "Deployment deck remains horizontally scrollable")
    check(hud.DeploymentCardCount == 4, "Selected squad creates four deployment cards")

    var first_id: String = hud.DeploymentCardCharacterIds[0]
    var first_card: Button = hud.get_node("DeploymentPanel/CardScroll/CardList/Card_" + first_id)
    check_button_states(first_card, "Deployment card")
    check("비용" in first_card.text, "Deployment card writes its deployment cost")
    check_button_states(hud.get_node("ResultPanel/RestartButton"), "Retry button")
    check_button_states(hud.get_node("ResultPanel/StageListButton"), "Stage-list button")

    var card_scroll: ScrollContainer = hud.get_node("DeploymentPanel/CardScroll")
    check(card_scroll.horizontal_scroll_mode != ScrollContainer.SCROLL_MODE_DISABLED,
        "Deployment deck enables horizontal scrolling")
    check(first_card.custom_minimum_size.x * 10 > card_scroll.size.x,
        "The stage maximum of ten cards exceeds the deck viewport and uses scrolling")

    hud.ShowResult(true, 12, 1)
    check(hud.get_node("ResultPanel").visible, "Result report becomes visible")
    check(hud.get_node("ResultPanel/ResultTitle").text == "작전 성공", "Victory result remains Korean")
    check(hud.get_node("ResultPanel/ResultTitle").modulate.g > hud.get_node("ResultPanel/ResultTitle").modulate.r,
        "Victory result uses the teal success color")
    check(hud.ResultSyncStatus == "Saving", "Result report starts in server-saving state")
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
