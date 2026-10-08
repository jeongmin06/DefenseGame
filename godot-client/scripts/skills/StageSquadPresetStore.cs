using DefenseGame.Client.Data;
using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace DefenseGame.Client.Skills;

[GlobalClass]
public partial class StageSquadPresetStore : RefCounted
{
    public const int SchemaVersion = 1;
    public const string DefaultStoragePath = "user://squad_presets.json";
    private string _storagePath = ProjectSettings.GlobalizePath(DefaultStoragePath);
    private bool _requiresExplicitOverwrite;
    private bool _migrationRequired;

    public string StoragePath => _storagePath;
    public string BackupPath => _storagePath + ".bak";
    public string TemporaryPath => _storagePath + ".tmp";

    public void ConfigureStoragePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Storage path cannot be empty.", nameof(path));
        _storagePath = path.StartsWith("user://", StringComparison.Ordinal) ? ProjectSettings.GlobalizePath(path) : Path.GetFullPath(path);
        _requiresExplicitOverwrite = false;
        _migrationRequired = false;
    }

    public StageSquadPresetLoadResult Load()
    {
        _requiresExplicitOverwrite = false;
        _migrationRequired = false;
        if (!File.Exists(_storagePath))
        {
            if (!File.Exists(BackupPath)) return Result(StageSquadPresetLoadStatus.Defaults, [], "No saved stage squads exist.");
            return LoadBackup("The primary stage squad file is missing.", "");
        }
        ParseOutcome primary = ReadDocument(_storagePath);
        if (primary.Kind == ParseKind.Valid) return Result(StageSquadPresetLoadStatus.Loaded, primary.Presets!, "Loaded stage squads.");
        if (primary.Kind == ParseKind.Future)
        {
            _migrationRequired = true;
            return Result(StageSquadPresetLoadStatus.MigrationRequired, [], $"Schema version {primary.Version} requires migration.", _storagePath);
        }
        string preserved = PreserveCorruptPrimary();
        _requiresExplicitOverwrite = true;
        return LoadBackup(primary.Message, preserved);
    }

    public StageSquadPresetSaveResult Save(Godot.Collections.Array<StageSquadPreset> presets) => SaveInternal(presets, false);
    public StageSquadPresetSaveResult SaveAfterRecovery(Godot.Collections.Array<StageSquadPreset> presets) => SaveInternal(presets, true);

    private StageSquadPresetSaveResult SaveInternal(Godot.Collections.Array<StageSquadPreset> presets, bool allowProtectedOverwrite)
    {
        if (_migrationRequired) return SaveResult(StageSquadPresetSaveStatus.MigrationRequired, "A future schema cannot be overwritten.");
        if (_requiresExplicitOverwrite && !allowProtectedOverwrite)
            return SaveResult(StageSquadPresetSaveStatus.BlockedProtectedData, "Recovered state requires explicit overwrite.");
        try
        {
            ValidateModel(presets);
            string? directory = Path.GetDirectoryName(_storagePath);
            if (string.IsNullOrEmpty(directory)) throw new IOException("Storage path needs a parent directory.");
            Directory.CreateDirectory(directory);
            WriteTemporary(presets);
            if (File.Exists(_storagePath))
            {
                ParseOutcome existing = ReadDocument(_storagePath);
                if (existing.Kind == ParseKind.Future)
                {
                    _migrationRequired = true;
                    return SaveResult(StageSquadPresetSaveStatus.MigrationRequired, "A future schema cannot be overwritten.");
                }
                if (existing.Kind != ParseKind.Valid)
                    return SaveResult(StageSquadPresetSaveStatus.BlockedProtectedData, "Load invalid existing data before recovery save.");
                File.Replace(TemporaryPath, _storagePath, BackupPath, true);
            }
            else File.Move(TemporaryPath, _storagePath);
            _requiresExplicitOverwrite = false;
            return SaveResult(StageSquadPresetSaveStatus.Saved, "Saved stage squads.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or ArgumentException or NotSupportedException)
        {
            return SaveResult(StageSquadPresetSaveStatus.Failed, ex.Message);
        }
        finally { TryDeleteTemporary(); }
    }

    private StageSquadPresetLoadResult LoadBackup(string primaryMessage, string preservedPath)
    {
        if (File.Exists(BackupPath))
        {
            ParseOutcome backup = ReadDocument(BackupPath);
            if (backup.Kind == ParseKind.Valid)
            {
                _requiresExplicitOverwrite = true;
                return Result(StageSquadPresetLoadStatus.RecoveredFromBackup, backup.Presets!,
                    $"Recovered backup after primary failure: {primaryMessage}", preservedPath, true);
            }
            if (backup.Kind == ParseKind.Future)
            {
                _migrationRequired = true;
                return Result(StageSquadPresetLoadStatus.MigrationRequired, [],
                    $"Backup schema version {backup.Version} requires migration.", BackupPath);
            }
            primaryMessage += $" Backup failed: {backup.Message}";
        }
        return Result(StageSquadPresetLoadStatus.Corrupt, [], primaryMessage, preservedPath, true);
    }

    private void WriteTemporary(Godot.Collections.Array<StageSquadPreset> presets)
    {
        using var stream = new FileStream(TemporaryPath, FileMode.Create, System.IO.FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", SchemaVersion);
            writer.WritePropertyName("stageSquadPresets");
            writer.WriteStartArray();
            foreach (StageSquadPreset preset in presets)
            {
                writer.WriteStartObject();
                writer.WriteString("stageId", preset.StageId);
                writer.WritePropertyName("characterIds"); writer.WriteStartArray();
                foreach (string id in preset.CharacterIds) writer.WriteStringValue(id);
                writer.WriteEndArray();
                writer.WriteString("updatedAtUtc", preset.UpdatedAtUtc);
                writer.WriteEndObject();
            }
            writer.WriteEndArray(); writer.WriteEndObject(); writer.Flush();
        }
        stream.Flush(true);
    }

    private static ParseOutcome ReadDocument(string path)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path));
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Root must be an object.");
            if (!root.TryGetProperty("schemaVersion", out JsonElement versionJson) || !TryInt(versionJson, out int version))
                throw new InvalidDataException("schemaVersion must be an integer.");
            if (version > SchemaVersion) return ParseOutcome.Future(version);
            if (version != SchemaVersion) throw new InvalidDataException($"Unsupported schemaVersion {version}.");
            Fields(root, "root", "schemaVersion", "stageSquadPresets");
            if (!root.TryGetProperty("stageSquadPresets", out JsonElement array) || array.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("stageSquadPresets must be an array.");
            var presets = new Godot.Collections.Array<StageSquadPreset>();
            var stages = new HashSet<string>(StringComparer.Ordinal);
            int index = 0;
            foreach (JsonElement item in array.EnumerateArray())
            {
                string context = $"stageSquadPresets[{index}]";
                if (item.ValueKind != JsonValueKind.Object) throw new InvalidDataException($"{context} must be an object.");
                Fields(item, context, "stageId", "characterIds", "updatedAtUtc");
                string stageId = String(item, "stageId", context);
                if (!stages.Add(stageId)) throw new InvalidDataException($"{context}.stageId is duplicated.");
                if (!item.TryGetProperty("characterIds", out JsonElement idsJson) || idsJson.ValueKind != JsonValueKind.Array)
                    throw new InvalidDataException($"{context}.characterIds must be an array.");
                var preset = new StageSquadPreset { StageId = stageId };
                int idIndex = 0;
                foreach (JsonElement idJson in idsJson.EnumerateArray())
                {
                    if (idJson.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(idJson.GetString()))
                        throw new InvalidDataException($"{context}.characterIds[{idIndex}] must be a nonempty string.");
                    preset.CharacterIds.Add(idJson.GetString()!); idIndex++;
                }
                preset.UpdatedAtUtc = String(item, "updatedAtUtc", context);
                if (!IsUtc(preset.UpdatedAtUtc)) throw new InvalidDataException($"{context}.updatedAtUtc must be UTC.");
                presets.Add(preset); index++;
            }
            return ParseOutcome.Valid(presets);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or NotSupportedException)
        { return ParseOutcome.Invalid(ex.Message); }
    }

    private static void ValidateModel(Godot.Collections.Array<StageSquadPreset> presets)
    {
        var stages = new HashSet<string>(StringComparer.Ordinal);
        foreach (StageSquadPreset? preset in presets)
        {
            if (preset is null) throw new InvalidDataException("A stage squad preset is missing.");
            Required(preset.StageId, "stageId");
            if (!stages.Add(preset.StageId)) throw new InvalidDataException("stageId is duplicated.");
            foreach (string id in preset.CharacterIds) Required(id, "characterId");
            Required(preset.UpdatedAtUtc, "updatedAtUtc");
            if (!IsUtc(preset.UpdatedAtUtc)) throw new InvalidDataException("updatedAtUtc must be UTC.");
        }
    }

    private static void Fields(JsonElement value, string context, params string[] allowed)
    {
        var names = new HashSet<string>(allowed, StringComparer.Ordinal);
        foreach (JsonProperty property in value.EnumerateObject())
            if (!names.Remove(property.Name)) throw new InvalidDataException($"{context}.{property.Name} is unknown or duplicated.");
        if (names.Count > 0) throw new InvalidDataException($"{context}.{names.Order(StringComparer.Ordinal).First()} is required.");
    }

    private static string String(JsonElement parent, string name, string context)
    {
        if (!parent.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException($"{context}.{name} must be a string.");
        string result = value.GetString() ?? ""; Required(result, $"{context}.{name}"); return result;
    }
    private static void Required(string value, string context) { if (string.IsNullOrWhiteSpace(value)) throw new InvalidDataException($"{context} cannot be empty."); }
    private static bool IsUtc(string value) => (value.EndsWith("Z", StringComparison.OrdinalIgnoreCase) || value.EndsWith("+00:00", StringComparison.Ordinal))
        && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset parsed) && parsed.Offset == TimeSpan.Zero;
    private static bool TryInt(JsonElement value, out int result)
    {
        result = 0;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out double number) || !double.IsFinite(number)
            || number != Math.Truncate(number) || number < int.MinValue || number > int.MaxValue) return false;
        result = (int)number; return true;
    }

    private string PreserveCorruptPrimary()
    {
        string directory = Path.GetDirectoryName(_storagePath) ?? ".";
        string stem = Path.GetFileNameWithoutExtension(_storagePath), extension = Path.GetExtension(_storagePath);
        string timestamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture);
        for (int i = 0; i < 1000; i++)
        {
            string candidate = Path.Combine(directory, $"{stem}.corrupt-{timestamp}{(i == 0 ? "" : $"-{i}")}{extension}");
            if (File.Exists(candidate)) continue;
            try { File.Move(_storagePath, candidate); return candidate; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException) { return ""; }
        }
        return "";
    }

    private StageSquadPresetLoadResult Result(StageSquadPresetLoadStatus status, Godot.Collections.Array<StageSquadPreset> presets,
        string message, string preserved = "", bool explicitOverwrite = false)
    {
        _requiresExplicitOverwrite = explicitOverwrite;
        return new StageSquadPresetLoadResult { Status = status, Presets = presets, Message = message,
            PreservedPath = preserved, RequiresExplicitOverwrite = explicitOverwrite };
    }
    private static StageSquadPresetSaveResult SaveResult(StageSquadPresetSaveStatus status, string message) => new() { Status = status, Message = message };
    private void TryDeleteTemporary() { try { if (File.Exists(TemporaryPath)) File.Delete(TemporaryPath); } catch { } }
    private enum ParseKind { Valid, Invalid, Future }
    private sealed record ParseOutcome(ParseKind Kind, Godot.Collections.Array<StageSquadPreset>? Presets, string Message, int Version)
    {
        public static ParseOutcome Valid(Godot.Collections.Array<StageSquadPreset> presets) => new(ParseKind.Valid, presets, "", SchemaVersion);
        public static ParseOutcome Invalid(string message) => new(ParseKind.Invalid, null, message, 0);
        public static ParseOutcome Future(int version) => new(ParseKind.Future, null, "", version);
    }
}
