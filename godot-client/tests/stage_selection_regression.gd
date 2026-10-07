extends SceneTree

var failures := 0
var elapsed_frames := 0

func _process(_delta):
    elapsed_frames += 1
    if elapsed_frames > 600:
        push_error("Stage selection QA timed out")
        quit(1)
    return false

func check(ok, message):
    if not ok:
        failures += 1
        push_error(message)

func ticks(count):
    for i in range(count): await process_frame

func _initialize(): run.call_deferred()

func run():
    change_scene_to_file("res://scenes/stage_select.tscn")
    await ticks(5)
    var selector = current_scene
    check(selector.StageCount == 3, "Three selectable stages")
    check(selector.get_node("UI/StagePanel/StageList").get_child_count() == 3, "Three generated buttons")
    check(selector.GetEnemyCount(0) == 21, "Stage 1 enemy count")
    check(selector.GetEnemyCount(1) == 30, "Stage 2 enemy count")
    check(selector.GetEnemyCount(2) == 42, "Stage 3 enemy count")

    selector.SelectStage(1)
    await ticks(5)
    check(current_scene.name == "StageOne", "Selection enters battle")
    check(current_scene.Definition.Id == "stage_02", "Selected definition reaches battle")
    check("STAGE 02" in current_scene.get_node("HUD/TopPanel/Title").text, "HUD shows selected stage")

    current_scene.RestartStage()
    await ticks(5)
    check(current_scene.Definition.Id == "stage_02", "Retry keeps selected stage")

    current_scene.ReturnToStageList()
    await ticks(5)
    check(current_scene.name == "StageSelect", "Result route returns to list")
    check(current_scene.StageCount == 3, "Catalog reloads after return")
    print("STAGE SELECT QA failures=", failures)
    quit(0 if failures == 0 else 1)
