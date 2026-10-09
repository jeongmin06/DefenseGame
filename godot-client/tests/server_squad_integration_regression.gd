extends SceneTree

var failures := 0
const SERVER_URL := "http://127.0.0.1:5099"

func check(ok, message):
    if not ok:
        failures += 1
        push_error(message)

func ticks(count):
    for i in range(count): await process_frame

func wait_for(predicate: Callable, attempt_limit := 120) -> bool:
    for i in range(attempt_limit):
        if predicate.call(): return true
        await create_timer(0.05).timeout
    return false

func _initialize(): run.call_deferred()

func run():
    ProjectSettings.set_setting("defense_game/server/base_url", SERVER_URL)
    ProjectSettings.set_setting("defense_game/player/development_user_id",
        "godot-integration-%d-%d" % [OS.get_process_id(), Time.get_ticks_usec()])

    change_scene_to_file("res://scenes/stage_select.tscn")
    await ticks(5)
    current_scene.SetSkillProfileStoragePathOverride(
        "/tmp/defense-server-squad-skills-%d-%d.json" % [OS.get_process_id(), Time.get_ticks_usec()])
    current_scene.SelectStage(0)
    var loaded := await wait_for(func(): return current_scene != null \
        and current_scene.name == "SquadFormation" and current_scene.IsServerReady)
    check(loaded, "Formation loads server state")
    if not loaded:
        quit(1)
        return
    var formation = current_scene
    check(formation.ServerRevision == 0, "New server formation starts at revision zero")
    check(formation.GetSelectedCharacterIds() == PackedStringArray([
        "starter_archer_a", "starter_archer_b", "starter_warrior_a", "starter_healer_a"]),
        "Missing server formation proposes all owned profiles")
    check(formation.ToggleCharacter("starter_healer_a"), "Formation can remove the healer before server save")
    check(formation.ContinueToSkills(), "Valid formation begins server save")
    var saved := await wait_for(func(): return current_scene != null and current_scene.name == "SkillLoadout")
    check(saved, "Successful server save enters skill preparation")
    if not saved:
        quit(1)
        return

    current_scene.ReturnToFormation()
    await ticks(5)
    current_scene.ReturnToStageList()
    await ticks(5)
    current_scene.SelectStage(0)
    var reloaded := await wait_for(func(): return current_scene != null \
        and current_scene.name == "SquadFormation" and current_scene.IsServerReady)
    check(reloaded, "Re-entry reloads formation from the server")
    if not reloaded:
        quit(1)
        return
    formation = current_scene
    check(formation.ServerRevision == 1, "Server save increments the stage revision")
    check(formation.GetSelectedCharacterIds() == PackedStringArray([
        "starter_archer_a", "starter_archer_b", "starter_warrior_a"]),
        "Server formation survives stage-list re-entry")

    print("SERVER SQUAD INTEGRATION QA failures=", failures)
    quit(0 if failures == 0 else 1)
