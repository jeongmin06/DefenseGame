extends SceneTree

var failures := 0
var root_path := "/tmp/defense-stage-squad-store-qa-%d" % Time.get_ticks_usec()

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
    var store = StageSquadPresetStore.new()
    store.ConfigureStoragePath(path)
    return store

func make_preset(stage_id: String, character_ids: Array[String], updated := "2026-10-08T01:02:03Z"):
    var preset = StageSquadPreset.new()
    preset.StageId = stage_id
    preset.CharacterIds.assign(character_ids)
    preset.UpdatedAtUtc = updated
    return preset

func valid_json(stage_id := "stage_01", character_ids := ["starter_archer_a", "starter_archer_b"]) -> String:
    return JSON.stringify({
        "schemaVersion": 1,
        "stageSquadPresets": [{
            "stageId": stage_id,
            "characterIds": character_ids,
            "updatedAtUtc": "2026-10-08T01:02:03Z"
        }]
    }, "  ")

func _initialize() -> void:
    run.call_deferred()

func run() -> void:
    check(DirAccess.make_dir_recursive_absolute(root_path) == OK, "Create isolated storage directory")

    var round_trip_path = root_path + "/round-trip.json"
    var store = make_store(round_trip_path)
    var missing = store.Load()
    check(missing.StatusCode == "Defaults" and missing.Presets.is_empty(), "Missing file returns empty defaults")

    var presets: Array[StageSquadPreset] = [
        make_preset("stage_02", ["starter_archer_b", "starter_archer_a"]),
        make_preset("stage_01", ["starter_archer_a"])
    ]
    check(store.Save(presets).StatusCode == "Saved", "First save succeeds")
    check(FileAccess.file_exists(round_trip_path) and not FileAccess.file_exists(round_trip_path + ".tmp"), "First save publishes only primary")
    var loaded = make_store(round_trip_path).Load()
    check(loaded.StatusCode == "Loaded" and loaded.Presets.size() == 2, "Multiple stages round trip")
    check(loaded.Presets[0].StageId == "stage_02" and loaded.Presets[1].StageId == "stage_01", "Stage order round trip")
    check(loaded.Presets[0].CharacterIds == ["starter_archer_b", "starter_archer_a"], "Character order round trip")

    var original_text = read_text(round_trip_path)
    presets[0].CharacterIds.reverse()
    check(store.Save(presets).StatusCode == "Saved", "Atomic replacement succeeds")
    check(read_text(round_trip_path + ".bak") == original_text, "Replacement preserves prior primary as backup")
    var second_primary = read_text(round_trip_path)
    presets[0].CharacterIds.reverse()
    check(store.Save(presets).StatusCode == "Saved", "Repeated replacement succeeds")
    check(read_text(round_trip_path + ".bak") == second_primary, "Repeated replacement refreshes backup")

    var before_primary = read_text(round_trip_path)
    var before_backup = read_text(round_trip_path + ".bak")
    check(DirAccess.make_dir_absolute(round_trip_path + ".tmp") == OK, "Create failing temporary target")
    check(store.Save(presets).StatusCode == "Failed", "Temporary write failure is reported")
    check(read_text(round_trip_path) == before_primary, "Failed save preserves primary")
    check(read_text(round_trip_path + ".bak") == before_backup, "Failed save preserves backup")
    DirAccess.remove_absolute(round_trip_path + ".tmp")

    var recovery_path = root_path + "/recovery.json"
    write_text(recovery_path + ".bak", valid_json())
    write_text(recovery_path, "{ broken json")
    var recovery_store = make_store(recovery_path)
    var recovered = recovery_store.Load()
    check(recovered.StatusCode == "RecoveredFromBackup", "Corrupt primary recovers valid backup")
    check(recovered.Presets[0].CharacterIds == ["starter_archer_a", "starter_archer_b"], "Backup content is preserved")
    check(recovered.RequiresExplicitOverwrite, "Recovery requires explicit overwrite")
    check(recovered.PreservedPath != "" and FileAccess.file_exists(recovered.PreservedPath), "Corrupt primary is preserved")
    check(recovery_store.Save(recovered.Presets).StatusCode == "BlockedProtectedData", "Recovered state does not auto overwrite")
    check(recovery_store.SaveAfterRecovery(recovered.Presets).StatusCode == "Saved", "Explicit recovery save succeeds")

    var double_path = root_path + "/double-corrupt.json"
    write_text(double_path, "not json")
    write_text(double_path + ".bak", "also not json")
    var double_store = make_store(double_path)
    var double_result = double_store.Load()
    check(double_result.StatusCode == "Corrupt" and double_result.Presets.is_empty(), "Two corrupt files return empty corrupt result")
    check(read_text(double_path + ".bak") == "also not json", "Corrupt backup remains unchanged")
    check(double_store.Save(double_result.Presets).StatusCode == "BlockedProtectedData", "Corrupt fallback cannot auto save")

    var future_path = root_path + "/future.json"
    var future_text = '{"schemaVersion":2,"futurePayload":{"keep":true}}'
    write_text(future_path, future_text)
    var future_store = make_store(future_path)
    var future = future_store.Load()
    check(future.StatusCode == "MigrationRequired", "Future schema requests migration")
    check(future_store.SaveAfterRecovery(future.Presets).StatusCode == "MigrationRequired", "Future schema cannot be overwritten")
    check(read_text(future_path) == future_text, "Future document bytes remain unchanged")

    var duplicate_path = root_path + "/duplicate-stage.json"
    var duplicate_doc = JSON.parse_string(valid_json())
    duplicate_doc.stageSquadPresets.append(duplicate_doc.stageSquadPresets[0].duplicate(true))
    write_text(duplicate_path, JSON.stringify(duplicate_doc))
    check(make_store(duplicate_path).Load().StatusCode == "Corrupt", "Duplicate stage ID is structural corruption")

    var content_path = root_path + "/content-invalid.json"
    write_text(content_path, valid_json("stage_01", ["removed_character", "removed_character", "starter_archer_a"]))
    var content = make_store(content_path).Load()
    check(content.StatusCode == "Loaded", "Unknown and duplicate character IDs are content validation concerns")
    check(content.Presets[0].CharacterIds == ["removed_character", "removed_character", "starter_archer_a"], "Content-invalid IDs load unchanged")

    print("STAGE SQUAD PRESET STORE QA failures=", failures)
    quit(0 if failures == 0 else 1)
