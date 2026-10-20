extends SceneTree

const CAPTURE_FPS := 30
const SERVER_URL := "http://127.0.0.1:5080"

var _failed := false

func _initialize():
    run.call_deferred()

func hold(seconds: float):
    for _frame in range(int(ceil(seconds * CAPTURE_FPS))):
        await process_frame

func wait_for(predicate: Callable, timeout_seconds := 20.0) -> bool:
    var frame_limit := int(ceil(timeout_seconds * CAPTURE_FPS))
    for _frame in range(frame_limit):
        if predicate.call():
            return true
        OS.delay_msec(5)
        await process_frame
    return predicate.call()

func require(condition: bool, message: String) -> bool:
    if condition:
        return true
    _failed = true
    push_error("CAPTURE: " + message)
    return false

func deploy(stage, character_id: String, required_points: float, cell: Vector2i, facing: int) -> bool:
    if not await wait_for(func(): return stage.DeploymentPoints >= required_points, 15.0):
        return require(false, "%s 배치 포인트 대기 실패" % character_id)
    var grid = stage.get_node("DeploymentGrid")
    var hud = stage.get_node("HUD")
    var drop_position = grid.CellToGlobal(cell)
    if not require(hud.BeginCharacterDrag(character_id, drop_position), "%s 드래그 시작 실패" % character_id):
        return false
    await hold(0.6)
    if not require(hud.CompleteCharacterDrag(drop_position), "%s 배치 드롭 실패" % character_id):
        return false
    await hold(0.6)
    if not require(grid.SelectFacing(facing), "%s 방향 확정 실패" % character_id):
        return false
    await hold(0.8)
    return true

func run():
    ProjectSettings.set_setting("defense_game/server/base_url", SERVER_URL)
    ProjectSettings.set_setting("defense_game/player/development_user_id",
        "capture-%d-%d" % [OS.get_process_id(), Time.get_ticks_usec()])
    ProjectSettings.set_setting("defense_game/progress/submit_in_headless", true)

    change_scene_to_file("res://scenes/stage_select.tscn")
    var ready := await wait_for(func(): return current_scene != null \
        and current_scene.name == "StageSelect" and current_scene.IsProgressReady)
    if not require(ready, "작전 진행도 조회 실패"):
        quit(1)
        return
    await hold(3.0)
    current_scene.ShowStageDescription(0)
    await hold(1.5)
    if not require(current_scene.SelectStage(0), "1스테이지 선택 실패"):
        quit(1)
        return

    ready = await wait_for(func(): return current_scene != null \
        and current_scene.name == "SquadFormation" and current_scene.IsServerReady)
    if not require(ready, "편성 서버 상태 조회 실패"):
        quit(1)
        return
    await hold(3.0)
    if not require(current_scene.ContinueToSkills(), "편성 저장 시작 실패"):
        quit(1)
        return

    ready = await wait_for(func(): return current_scene != null \
        and current_scene.name == "SkillLoadout" and current_scene.IsServerReady)
    if not require(ready, "스킬 프로필 조회 실패"):
        quit(1)
        return
    current_scene.SelectCharacter("starter_archer_a")
    await hold(1.5)
    current_scene.SelectCharacter("starter_warrior_a")
    await hold(1.5)
    current_scene.SelectCharacter("starter_healer_a")
    await hold(1.5)
    current_scene.SelectCharacter("starter_archer_a")
    if not require(current_scene.StartBattle(), "스킬 저장과 전투 시작 실패"):
        quit(1)
        return

    ready = await wait_for(func(): return current_scene != null and current_scene.name == "StageOne")
    if not require(ready, "전투 씬 진입 실패"):
        quit(1)
        return
    var stage = current_scene
    await hold(0.8)
    var grid = stage.get_node("DeploymentGrid")
    var hud = stage.get_node("HUD")
    if not require(hud.BeginCharacterDrag("starter_archer_a", grid.CellToGlobal(Vector2i(1, 3))),
        "궁수 경로 타일 미리보기 시작 실패"):
        quit(1)
        return
    if not require(not hud.DragPlacementValid and "지형" in hud.DragPlacementStatus,
        "궁수 경로 타일 미리보기 판정 실패"):
        quit(1)
        return
    await hold(0.8)
    hud.CancelCharacterDrag()
    await hold(0.3)
    if not await deploy(stage, "starter_archer_a", 10.0, Vector2i(4, 2), 0):
        quit(1)
        return
    if not await deploy(stage, "starter_archer_b", 9.0, Vector2i(9, 3), 2):
        quit(1)
        return
    if not await deploy(stage, "starter_warrior_a", 12.0, Vector2i(6, 2), 0):
        quit(1)
        return
    if not await deploy(stage, "starter_healer_a", 11.0, Vector2i(5, 2), 0):
        quit(1)
        return

    ready = await wait_for(func(): return stage.get_node("HUD/ResultPanel").visible, 150.0)
    if not require(ready, "전투 결과 대기 시간 초과"):
        quit(1)
        return
    ready = await wait_for(func(): return stage.ResultSyncStatusCode == "Saved", 20.0)
    if not require(ready, "전투 결과 서버 저장 실패"):
        quit(1)
        return
    await hold(4.0)
    stage.ReturnToStageList()

    ready = await wait_for(func(): return current_scene != null \
        and current_scene.name == "StageSelect" and current_scene.IsProgressReady)
    if not require(ready, "결과 저장 후 작전 진행도 재조회 실패"):
        quit(1)
        return
    require("클리어" in current_scene.GetStageButtonText(0), "1스테이지 클리어 표시 누락")
    require(not current_scene.IsStageLocked(1), "2스테이지 해금 실패")
    await hold(4.0)

    print("SMOKE REVIEW CAPTURE result=", "failed" if _failed else "success")
    quit(1 if _failed else 0)
