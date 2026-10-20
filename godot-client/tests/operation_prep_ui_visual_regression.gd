extends SceneTree

const BACKGROUND_PATH := "res://assets/backgrounds/operation_preparation_ruined_city.png"

var failures := 0
var elapsed_frames := 0
var root_path := "/tmp/defense-operation-ui-%d-%d" % [OS.get_process_id(), Time.get_ticks_usec()]

func _process(_delta):
    elapsed_frames += 1
    if elapsed_frames > 900:
        push_error("Operation preparation UI QA timed out")
        quit(1)
    return false

func check(ok, message):
    if not ok:
        failures += 1
        push_error(message)

func ticks(count):
    for i in range(count):
        await process_frame

func check_button_states(button: Button, label: String):
    for state in ["normal", "hover", "focus", "disabled"]:
        check(button.get_theme_stylebox(state) is StyleBoxFlat, "%s has %s operation style" % [label, state])

func _initialize():
    run.call_deferred()

func run():
    check(DirAccess.make_dir_recursive_absolute(root_path) == OK, "Create isolated operation UI directory")
    var transition := change_scene_to_file("res://scenes/stage_select.tscn")
    if transition != OK:
        check(false, "Stage selection scene starts")
        quit(1)
        return
    await ticks(5)
    if current_scene == null or current_scene.name != "StageSelect":
        check(false, "Stage selection scene loads")
        quit(1)
        return
    current_scene.SetStageSquadStoragePathOverride(root_path + "/squad.json")
    current_scene.SetSkillProfileStoragePathOverride(root_path + "/skills.json")
    current_scene.SelectStage(0)
    await ticks(5)

    var formation = current_scene
    if formation == null or formation.name != "SquadFormation":
        check(false, "Formation screen opens")
        quit(1)
        return
    var formation_background: TextureRect = formation.get_node("UI/Background")
    check(formation_background.texture.resource_path == BACKGROUND_PATH, "Formation uses approved ruined-city background")
    check("stage_" not in formation.get_node("UI/StageLabel").text, "Formation header hides internal stage id")
    check(formation.get_node("UI/ReadabilityOverlay").color.a >= 0.55, "Formation has readability overlay")
    check(formation.get_node_or_null("UI/MainPanel/OwnedPanel") != null, "Formation has owned-cat panel")
    check(formation.get_node_or_null("UI/MainPanel/SelectedPanel") != null, "Formation has selected-order panel")
    check(formation.get_node("UI/MainPanel/OwnedScroll") is ScrollContainer, "Formation owned list scrolls")
    check(formation.get_node("UI/MainPanel/SelectedScroll") is ScrollContainer, "Formation selected list scrolls")
    var owned_button: Button = formation.get_node("UI/MainPanel/OwnedScroll/OwnedList/Owned_starter_archer_a")
    check("출전 중" in owned_button.text, "Formation selected state is written as text")
    check("starter_archer_a" not in owned_button.text, "Formation card hides internal character id")
    check("배치 비용" in owned_button.text, "Formation card exposes deployment cost")
    check_button_states(owned_button, "Owned cat button")
    check(owned_button.mouse_default_cursor_shape == Control.CURSOR_POINTING_HAND,
        "Operation button uses a pointing-hand cursor")
    check(owned_button.has_meta("operation_ui_interaction_bound"),
        "Operation button binds tactile interaction once")
    owned_button.emit_signal("button_down")
    check(owned_button.scale.x < 1.0, "Operation button shrinks while pressed")
    owned_button.emit_signal("button_up")
    check(owned_button.scale.x >= 1.0, "Operation button restores after release")
    check_button_states(formation.get_node("UI/MainPanel/Actions/ContinueButton"), "Formation continue button")

    check(formation.ToggleCharacter("starter_archer_a"), "Formation removes a cat for focus regression")
    await ticks(3)
    owned_button = formation.get_node("UI/MainPanel/OwnedScroll/OwnedList/Owned_starter_archer_a")
    check(formation.get_viewport().gui_get_focus_owner() == owned_button,
        "Formation restores focus to the toggled cat")
    check(formation.ToggleCharacter("starter_archer_a"), "Formation restores the removed cat")
    await ticks(3)
    check(formation.MoveSelectedUp("starter_archer_a"), "Formation reorders the focused cat")
    await ticks(3)
    var move_focus = formation.get_viewport().gui_get_focus_owner()
    check(move_focus != null and move_focus.name == "MoveDown",
        "Formation keeps focus on a valid adjacent order action")

    check(formation.ContinueToSkills(), "Formation continues to skill preparation")
    await ticks(5)
    var skills = current_scene
    if skills == null or skills.name != "SkillLoadout":
        check(false, "Skill preparation screen opens")
        quit(1)
        return
    var skill_background: TextureRect = skills.get_node("UI/Background")
    check(skill_background.texture.resource_path == BACKGROUND_PATH, "Skill preparation shares approved background")
    check("stage_" not in skills.get_node("UI/StageLabel").text, "Skill header hides internal stage id")
    check(skills.get_node("UI/ReadabilityOverlay").color.a >= 0.60, "Skill preparation has readability overlay")
    check(skills.get_node_or_null("UI/MainPanel/ActiveCard/ActiveLabel") != null, "Required active label path remains stable")
    check(skills.get_node_or_null("UI/MainPanel/SupportScroll/CandidateList") != null, "Required candidate list path remains stable")
    check(skills.get_node_or_null("UI/MainPanel/CharacterPanel") != null, "Skill preparation has character section")
    check(skills.get_node_or_null("UI/MainPanel/BudgetPanel") != null, "Skill preparation has point-budget section")

    check(skills.get_node("UI/MainPanel/CharacterScroll") is ScrollContainer, "Skill character list scrolls")
    check(skills.SelectCharacter("starter_archer_a"), "Skill screen selects the archer under test")
    await ticks(3)
    var character_button: Button = skills.get_node("UI/MainPanel/CharacterScroll/CharacterList/Character_starter_archer_a")
    check("선택됨" in character_button.text, "Current character state is written as text")
    check("starter_archer_a" not in character_button.text, "Skill character card hides internal id")
    check("보조" in character_button.text, "Skill character card exposes linked support count")
    check_button_states(character_button, "Character button")
    var active_list: HBoxContainer = skills.get_node("UI/MainPanel/ActiveCard/ActiveList")
    for active_button in active_list.get_children():
        check(active_button.position.x + active_button.size.x <= active_list.size.x + 1.0,
            "Active skill button stays inside the active card")
    check(skills.TrySelectActive("basic_arrow"), "Skill screen reselects the active skill")
    await ticks(3)
    var active_focus = skills.get_viewport().gui_get_focus_owner()
    check(active_focus != null and active_focus.name == "Active_basic_arrow",
        "Skill screen restores focus after active selection")
    var support_button: Button = skills.get_node("UI/MainPanel/SupportScroll/CandidateList/Support_multiple_projectiles")
    check_button_states(support_button, "Support skill button")
    check(skills.TryToggleSupport("multiple_projectiles"), "Skill screen toggles a support for focus regression")
    await ticks(3)
    var support_focus = skills.get_viewport().gui_get_focus_owner()
    check(support_focus != null and support_focus.name == "Support_multiple_projectiles",
        "Skill screen restores focus after support selection")
    check_button_states(skills.get_node("UI/MainPanel/Actions/SaveButton"), "Save button")
    check_button_states(skills.get_node("UI/MainPanel/Actions/StartButton"), "Start button")

    print("OPERATION PREP UI VISUAL QA failures=", failures)
    quit(0 if failures == 0 else 1)
