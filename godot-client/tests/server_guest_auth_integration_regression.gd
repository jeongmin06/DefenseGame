extends SceneTree

const SERVER_URL := "http://127.0.0.1:5099"
var session_path := "user://guest_session.json"

var failures := 0
var elapsed_frames := 0

func _process(_delta):
    elapsed_frames += 1
    if elapsed_frames > 6000:
        push_error("Guest authentication integration QA timed out")
        quit(1)
    return false

func check(ok, message):
    if not ok:
        failures += 1
        push_error(message)

func wait_for(predicate: Callable, attempt_limit := 240) -> bool:
    for i in range(attempt_limit):
        if predicate.call(): return true
        await create_timer(0.05).timeout
    return predicate.call()

func remove_session():
    var absolute_path := ProjectSettings.globalize_path(session_path) \
        if session_path.begins_with("user://") else session_path
    if FileAccess.file_exists(session_path):
        DirAccess.remove_absolute(absolute_path)
    if FileAccess.file_exists(session_path + ".tmp"):
        DirAccess.remove_absolute(absolute_path + ".tmp")

func _initialize(): run.call_deferred()

func run():
    var configured_session_path := OS.get_environment("DEFENSEGAME_GUEST_SESSION_PATH")
    if not configured_session_path.is_empty():
        session_path = configured_session_path
    remove_session()
    ProjectSettings.set_setting("defense_game/server/base_url", SERVER_URL)
    ProjectSettings.set_setting("defense_game/server/auth_mode", "guest")

    change_scene_to_file("res://scenes/stage_select.tscn")
    var progress_loaded := await wait_for(func(): return current_scene != null \
        and current_scene.name == "StageSelect" and current_scene.IsProgressReady)
    check(progress_loaded, "Guest token loads player progress")
    check(FileAccess.file_exists(session_path), "Guest session is persisted under user storage")

    var session = JSON.parse_string(FileAccess.get_file_as_string(session_path))
    check(session is Dictionary, "Guest session file contains JSON object")
    if session is Dictionary:
        check(str(session.get("playerId", "")).begins_with("guest_"),
            "Guest session stores server-issued player ID")
        check(not str(session.get("accessToken", "")).is_empty(),
            "Guest session stores access token without logging it")

    check(progress_loaded and current_scene.SelectStage(0),
        "Authenticated progress enters formation")
    var formation_loaded := await wait_for(func(): return current_scene != null \
        and current_scene.name == "SquadFormation" and current_scene.IsServerReady)
    check(formation_loaded, "Authenticated request loads formation")
    if not formation_loaded:
        remove_session()
        quit(1)
        return

    check(current_scene.ContinueToSkills(), "Authenticated request saves formation")
    var skills_loaded := await wait_for(func(): return current_scene != null \
        and current_scene.name == "SkillLoadout" and current_scene.IsServerReady)
    check(skills_loaded, "Authenticated request loads skill profile")
    if skills_loaded:
        check(current_scene.SaveProfile(), "Authenticated request starts skill profile save")
        var skill_saved := await wait_for(func(): return current_scene.SaveStatusCode == "Saved")
        check(skill_saved, "Authenticated request saves skill profile")

    remove_session()
    print("SERVER GUEST AUTH INTEGRATION QA failures=", failures)
    quit(0 if failures == 0 else 1)
