extends SceneTree

var failures := 0
var root_path := "/tmp/defense-skill-profile-qa-%d" % Time.get_ticks_usec()

func check(ok: bool, message: String) -> void:
    if not ok:
        failures += 1
        push_error(message)

func write_text(path: String, content: String) -> void:
    var file = FileAccess.open(path, FileAccess.WRITE)
    check(file != null, "Open test file for writing: " + path)
    if file != null:
        file.store_string(content)
        file.flush()
        file.close()

func read_text(path: String) -> String:
    return FileAccess.get_file_as_string(path)

func make_store(path: String):
    var store = SkillProfileStore.new()
    store.ConfigureStoragePath(path)
    return store

func make_progress(level: int, points: int, ids: Array[String]):
    var progress = PlayerSkillProgress.new()
    progress.PlayerLevel = level
    progress.UnlockedPoints = points
    progress.OwnedSkillIds.assign(ids)
    return progress

func make_preset(character_id: String, active_id: String, supports: Array[String], allocated: int, updated: String):
    var preset = CatSkillPreset.new()
    preset.CharacterId = character_id
    preset.ActiveSkillId = active_id
    preset.SupportSkillIds.assign(supports)
    preset.AllocatedPoints = allocated
    preset.UpdatedAtUtc = updated
    return preset

func valid_json(character_id := "starter_archer_a", active_id := "basic_arrow") -> String:
    return JSON.stringify({
        "schemaVersion": 1,
        "playerSkillProgress": {
            "playerLevel": 5,
            "unlockedPoints": 5,
            "ownedSkillIds": ["basic_arrow", "removed_owned_skill"]
        },
        "catSkillPresets": [{
            "characterId": character_id,
            "activeSkillId": active_id,
            "supportSkillIds": ["removed_support_b", "removed_support_a"],
            "allocatedPoints": 2,
            "updatedAtUtc": "2026-10-08T01:02:03Z"
        }]
    }, "  ")

func _initialize() -> void:
    run.call_deferred()

func run() -> void:
    check(DirAccess.make_dir_recursive_absolute(root_path) == OK, "Create isolated storage directory")
    var defaults = load("res://data/player/defaults.tres")

    var round_trip_path = root_path + "/round-trip.json"
    var store = make_store(round_trip_path)
    var missing = store.Load()
    check(missing.StatusCode == "Defaults", "Missing file returns defaults status")
    check(missing.Progress.PlayerLevel == 5 and missing.Presets.size() == 4, "Missing file clones generated defaults")
    missing.Progress.PlayerLevel = 99
    missing.Presets[0].SupportSkillIds.clear()
    check(defaults.Progress.PlayerLevel == 5 and defaults.InitialPresets[0].SupportSkillIds.size() == 2, "Default resources are never mutated")

    var progress = make_progress(7, 6, ["fire_infusion", "removed_owned_skill", "basic_arrow"])
    var presets: Array[CatSkillPreset] = [
        make_preset("starter_archer_b", "removed_active", ["removed_support_b", "removed_support_a"], 2, "2026-10-08T03:02:01Z"),
        make_preset("starter_archer_a", "basic_arrow", ["fire_infusion"], 1, "2026-10-08T03:02:02Z")
    ]
    var first_save = store.Save(progress, presets)
    check(first_save.StatusCode == "Saved", "First save succeeds")
    check(FileAccess.file_exists(round_trip_path) and not FileAccess.file_exists(round_trip_path + ".tmp"), "First save publishes only the primary file")

    var reloaded = make_store(round_trip_path).Load()
    check(reloaded.StatusCode == "Loaded", "Saved profile loads normally")
    check(reloaded.Progress.PlayerLevel == 7 and reloaded.Progress.UnlockedPoints == 6, "Progress round trip")
    check(reloaded.Progress.OwnedSkillIds == ["fire_infusion", "removed_owned_skill", "basic_arrow"], "Owned skill order round trip")
    check(reloaded.Presets[0].CharacterId == "starter_archer_b" and reloaded.Presets[1].CharacterId == "starter_archer_a", "Preset order round trip")
    check(reloaded.Presets[0].ActiveSkillId == "removed_active", "Unknown active ID is preserved")
    check(reloaded.Presets[0].SupportSkillIds == ["removed_support_b", "removed_support_a"], "Unknown support IDs and order are preserved")
    check(reloaded.Presets[0].AllocatedPoints == 2 and reloaded.Presets[0].UpdatedAtUtc == "2026-10-08T03:02:01Z", "Preset metadata round trip")

    var original_text = read_text(round_trip_path)
    progress.PlayerLevel = 8
    progress.OwnedSkillIds.reverse()
    var replace_save = store.Save(progress, presets)
    check(replace_save.StatusCode == "Saved", "Atomic replacement succeeds")
    check(read_text(round_trip_path + ".bak") == original_text, "Successful replacement preserves the prior valid primary as backup")
    check(make_store(round_trip_path).Load().Progress.PlayerLevel == 8, "Replacement publishes the new document")

    var before_invalid_model = read_text(round_trip_path)
    var duplicate_presets: Array[CatSkillPreset] = []
    duplicate_presets.assign(presets)
    duplicate_presets.append(presets[0])
    check(store.Save(progress, duplicate_presets).StatusCode == "Failed", "Invalid in-memory schema returns save failure")
    check(read_text(round_trip_path) == before_invalid_model, "Schema failure does not modify the primary")

    var second_primary = read_text(round_trip_path)
    progress.PlayerLevel = 9
    check(store.Save(progress, presets).StatusCode == "Saved", "Replacement succeeds when a backup already exists")
    check(read_text(round_trip_path + ".bak") == second_primary, "Existing backup is atomically replaced with the latest prior primary")

    var before_failed_primary = read_text(round_trip_path)
    var before_failed_backup = read_text(round_trip_path + ".bak")
    check(DirAccess.make_dir_absolute(round_trip_path + ".tmp") == OK, "Create an unwritable temporary target")
    progress.PlayerLevel = 10
    var failed_save = store.Save(progress, presets)
    check(failed_save.StatusCode == "Failed", "Temporary write failure has an explicit status")
    check(read_text(round_trip_path) == before_failed_primary, "Failed save preserves the primary")
    check(read_text(round_trip_path + ".bak") == before_failed_backup, "Failed save preserves the backup")
    DirAccess.remove_absolute(round_trip_path + ".tmp")

    var recovery_path = root_path + "/recovery.json"
    write_text(recovery_path + ".bak", valid_json())
    write_text(recovery_path, "{ broken json")
    var recovery_store = make_store(recovery_path)
    var recovered = recovery_store.Load()
    check(recovered.StatusCode == "RecoveredFromBackup", "Corrupt primary recovers a valid backup")
    check(recovered.Progress.OwnedSkillIds == ["basic_arrow", "removed_owned_skill"], "Backup data loads unchanged")
    check(recovered.RequiresExplicitOverwrite, "Backup recovery requires explicit overwrite")
    check(recovered.PreservedPath != "" and FileAccess.file_exists(recovered.PreservedPath), "Corrupt primary is preserved under a diagnostic name")
    check(not FileAccess.file_exists(recovery_path), "Corrupt primary is removed only by preservation rename")
    check(recovery_store.Save(recovered.Progress, recovered.Presets).StatusCode == "BlockedProtectedData", "Recovered state cannot auto-save")
    check(recovery_store.SaveAfterRecovery(recovered.Progress, recovered.Presets).StatusCode == "Saved", "Explicit save may publish recovered data")
    check(FileAccess.file_exists(recovered.PreservedPath), "Explicit recovery save retains the corrupt artifact")

    var double_corrupt_path = root_path + "/double-corrupt.json"
    write_text(double_corrupt_path, "not json")
    write_text(double_corrupt_path + ".bak", "also not json")
    var double_store = make_store(double_corrupt_path)
    var double_corrupt = double_store.Load()
    check(double_corrupt.StatusCode == "Corrupt", "Corrupt primary and backup return corrupt status")
    check(double_corrupt.Progress.PlayerLevel == 5 and double_corrupt.Presets.size() == 4, "Corrupt files return in-memory generated defaults")
    check(double_corrupt.PreservedPath != "" and FileAccess.file_exists(double_corrupt.PreservedPath), "Corrupt primary remains diagnosable")
    check(read_text(double_corrupt_path + ".bak") == "also not json", "Corrupt backup is not overwritten")
    check(double_store.Save(double_corrupt.Progress, double_corrupt.Presets).StatusCode == "BlockedProtectedData", "Corrupt fallback defaults cannot auto-save")

    var future_path = root_path + "/future.json"
    var future_text = '{"schemaVersion":2,"futurePayload":{"keep":true}}'
    write_text(future_path, future_text)
    var future_store = make_store(future_path)
    var future = future_store.Load()
    check(future.StatusCode == "MigrationRequired", "Future schema returns migration status")
    check(read_text(future_path) == future_text and future.PreservedPath == future_path, "Future document stays at its original path")
    check(future_store.SaveAfterRecovery(future.Progress, future.Presets).StatusCode == "MigrationRequired", "Future document cannot be overwritten")
    check(read_text(future_path) == future_text, "Blocked future save preserves the original bytes")

    var duplicate_path = root_path + "/duplicate.json"
    var duplicate_doc = JSON.parse_string(valid_json())
    duplicate_doc.catSkillPresets.append(duplicate_doc.catSkillPresets[0].duplicate(true))
    write_text(duplicate_path, JSON.stringify(duplicate_doc))
    var duplicate = make_store(duplicate_path).Load()
    check(duplicate.StatusCode == "Corrupt" and duplicate.PreservedPath != "", "Duplicate characterId is a schema error")

    var negative_path = root_path + "/negative.json"
    var negative_doc = JSON.parse_string(valid_json())
    negative_doc.playerSkillProgress.unlockedPoints = -1
    write_text(negative_path, JSON.stringify(negative_doc))
    check(make_store(negative_path).Load().StatusCode == "Corrupt", "Negative points are a schema error")

    var missing_field_path = root_path + "/missing-field.json"
    var missing_field_doc = JSON.parse_string(valid_json())
    missing_field_doc.catSkillPresets[0].erase("activeSkillId")
    write_text(missing_field_path, JSON.stringify(missing_field_doc))
    check(make_store(missing_field_path).Load().StatusCode == "Corrupt", "Missing required fields are schema errors")

    var wrong_type_path = root_path + "/wrong-type.json"
    var wrong_type_doc = JSON.parse_string(valid_json())
    wrong_type_doc.playerSkillProgress.ownedSkillIds = "basic_arrow"
    write_text(wrong_type_path, JSON.stringify(wrong_type_doc))
    check(make_store(wrong_type_path).Load().StatusCode == "Corrupt", "Wrong field types are schema errors")

    var content_path = root_path + "/content-invalid.json"
    var content_doc = JSON.parse_string(valid_json("removed_character", "removed_active"))
    content_doc.playerSkillProgress.unlockedPoints = 0
    content_doc.catSkillPresets[0].supportSkillIds = ["missing", "missing", "a", "b", "c", "d"]
    content_doc.catSkillPresets[0].allocatedPoints = 42
    write_text(content_path, JSON.stringify(content_doc))
    var content_result = make_store(content_path).Load()
    check(content_result.StatusCode == "Loaded", "Content incompatibility is not file corruption: " + content_result.Message)
    check(content_result.Presets[0].CharacterId == "removed_character" and content_result.Presets[0].ActiveSkillId == "removed_active", "Unknown character and active IDs are preserved")
    check(content_result.Presets[0].SupportSkillIds == ["missing", "missing", "a", "b", "c", "d"], "Duplicate and over-limit support IDs are preserved")
    check(content_result.Presets[0].AllocatedPoints == 42 and content_result.Progress.UnlockedPoints == 0, "Budget-invalid values are preserved for later validation")

    print("SKILL PROFILE STORE QA failures=", failures)
    quit(0 if failures == 0 else 1)
