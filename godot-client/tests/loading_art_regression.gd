extends SceneTree
var failures := 0
func check(ok, message):
    if not ok:
        failures += 1
        push_error(message)
func ticks(count):
    for i in range(count): await process_frame
func _initialize(): run.call_deferred()
func run():
    change_scene_to_file("res://scenes/stage_select.tscn")
    await ticks(3)
    for i in range(300):
        if not root.has_node("LoadingScreen"): break
        await process_frame
    check(not root.has_node("LoadingScreen"), "Boot loading finishes")
    check(current_scene.name == "StageSelect", "Boot retains stage selection")
    check(current_scene.process_mode == Node.PROCESS_MODE_INHERIT, "Boot restores input and processing")
    change_scene_to_file("res://scenes/stage_select.tscn")
    await ticks(3)
    check(not root.has_node("LoadingScreen"), "Returning to selection does not repeat boot")
    var loading = load("res://scenes/loading_screen.tscn").instantiate()
    loading.TargetScene = "res://missing_scene_for_loading_qa.tscn"
    root.add_child(loading)
    await ticks(2)
    check(loading.get_node("UI/Retry").visible, "Missing scene exposes retry")
    check(loading.get_node("UI/Back").visible, "Missing scene exposes return route")
    check("불러오지 못" in loading.get_node("UI/Status").text, "Failure is explicit")
    loading.TargetScene = "res://scenes/stage_one.tscn"
    loading.get_node("UI/Retry").pressed.emit()
    for i in range(300):
        if not is_instance_valid(loading): break
        await process_frame
    check(not is_instance_valid(loading), "Retry loads valid resource and closes overlay")
    change_scene_to_file("res://scenes/stage_one.tscn")
    await ticks(5)
    check(current_scene.has_node("BattlefieldArt"), "Combat art is integrated")
    check(current_scene.get_node("BattlefieldArt").z_index == 1, "Art stays below combat entities")
    check(current_scene.SecondsUntilFirstWave > 9, "Battle countdown starts after scene entry")
    print("LOADING ART QA failures=", failures)
    quit(0 if failures == 0 else 1)
