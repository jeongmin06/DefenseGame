using DefenseGame.Client.Data;
using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace DefenseGame.Client.Skills;

// Player state storage only. Content compatibility remains the responsibility of SquadSkillValidator.
[GlobalClass]
public partial class SkillProfileStore : RefCounted
{
    public const int SchemaVersion = 1;
    public const string DefaultStoragePath = "user://skill_profile.json";
    public const string DefaultResourcePath = "res://data/player/defaults.tres";

    private string _storagePath = ProjectSettings.GlobalizePath(DefaultStoragePath);
    private bool _requiresExplicitOverwrite;
    private bool _migrationRequired;

    public string StoragePath => _storagePath;
    public string BackupPath => _storagePath + ".bak";
    public string TemporaryPath => _storagePath + ".tmp";

    public void ConfigureStoragePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Storage path cannot be empty.", nameof(path));
        _storagePath = path.StartsWith("user://", StringComparison.Ordinal)
            ? ProjectSettings.GlobalizePath(path)
            : Path.GetFullPath(path);
        _requiresExplicitOverwrite = false;
        _migrationRequired = false;
    }

    public SkillProfileLoadResult Load()
    {
        PlayerSkillDefaults defaults = ResourceLoader.Load<PlayerSkillDefaults>(DefaultResourcePath)
            ?? throw new InvalidOperationException($"Could not load player defaults at '{DefaultResourcePath}'.");
        _requiresExplicitOverwrite = false;
        _migrationRequired = false;

        if (!File.Exists(_storagePath))
        {
            if (!File.Exists(BackupPath))
                return DefaultsResult(defaults, SkillProfileLoadStatus.Defaults, "No saved skill profile exists.");
            return LoadBackup(defaults, "The primary skill profile is missing.", "");
        }

        ParseOutcome primary = ReadDocument(_storagePath);
        if (primary.Kind == ParseKind.Valid)
            return LoadedResult(primary, SkillProfileLoadStatus.Loaded, "Loaded the saved skill profile.");
        if (primary.Kind == ParseKind.Future)
        {
            _migrationRequired = true;
            return DefaultsResult(defaults, SkillProfileLoadStatus.MigrationRequired,
                $"Skill profile schema version {primary.Version} requires migration.", _storagePath);
        }

        string preservedPath = PreserveCorruptPrimary();
        _requiresExplicitOverwrite = true;
        return LoadBackup(defaults, primary.Message, preservedPath);
    }

    public SkillProfileSaveResult Save(
        PlayerSkillProgress progress,
        Godot.Collections.Array<CatSkillPreset> presets) => SaveInternal(progress, presets, false);

    public SkillProfileSaveResult SaveAfterRecovery(
        PlayerSkillProgress progress,
        Godot.Collections.Array<CatSkillPreset> presets) => SaveInternal(progress, presets, true);

    private SkillProfileSaveResult SaveInternal(
        PlayerSkillProgress progress,
        Godot.Collections.Array<CatSkillPreset> presets,
        bool allowProtectedOverwrite)
    {
        if (_migrationRequired)
            return SaveResult(SkillProfileSaveStatus.MigrationRequired,
                "A future skill profile version is present and cannot be overwritten.");
        if (_requiresExplicitOverwrite && !allowProtectedOverwrite)
            return SaveResult(SkillProfileSaveStatus.BlockedProtectedData,
                "Recovered defaults require an explicit overwrite before saving.");

        try
        {
            ValidateModel(progress, presets);
            string? directory = Path.GetDirectoryName(_storagePath);
            if (string.IsNullOrEmpty(directory))
                throw new IOException("Storage path must have a parent directory.");
            Directory.CreateDirectory(directory);

            WriteTemporary(progress, presets);
            if (File.Exists(_storagePath))
            {
                ParseOutcome existing = ReadDocument(_storagePath);
                if (existing.Kind == ParseKind.Future)
                {
                    _migrationRequired = true;
                    return SaveResult(SkillProfileSaveStatus.MigrationRequired,
                        "A future skill profile version is present and cannot be overwritten.");
                }
                if (existing.Kind != ParseKind.Valid)
                    return SaveResult(SkillProfileSaveStatus.BlockedProtectedData,
                        "The existing skill profile is invalid and must be loaded for recovery before saving.");

                File.Replace(TemporaryPath, _storagePath, BackupPath, true);
            }
            else
            {
                File.Move(TemporaryPath, _storagePath);
            }

            _requiresExplicitOverwrite = false;
            return SaveResult(SkillProfileSaveStatus.Saved, "Saved the skill profile.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
                                   or ArgumentException or InvalidDataException or NotSupportedException)
        {
            return SaveResult(SkillProfileSaveStatus.Failed, ex.Message);
        }
        finally
        {
            TryDeleteTemporary();
        }
    }

    private SkillProfileLoadResult LoadBackup(PlayerSkillDefaults defaults, string primaryMessage, string preservedPath)
    {
        if (File.Exists(BackupPath))
        {
            ParseOutcome backup = ReadDocument(BackupPath);
            if (backup.Kind == ParseKind.Valid)
            {
                _requiresExplicitOverwrite = true;
                return LoadedResult(backup, SkillProfileLoadStatus.RecoveredFromBackup,
                    $"Recovered the backup after the primary profile failed: {primaryMessage}", preservedPath, true);
            }
            if (backup.Kind == ParseKind.Future)
            {
                _migrationRequired = true;
                return DefaultsResult(defaults, SkillProfileLoadStatus.MigrationRequired,
                    $"Backup schema version {backup.Version} requires migration.", BackupPath);
            }
            primaryMessage += $" Backup failed: {backup.Message}";
        }

        return DefaultsResult(defaults, SkillProfileLoadStatus.Corrupt, primaryMessage, preservedPath, true);
    }

    private string PreserveCorruptPrimary()
    {
        string directory = Path.GetDirectoryName(_storagePath) ?? ".";
        string stem = Path.GetFileNameWithoutExtension(_storagePath);
        string extension = Path.GetExtension(_storagePath);
        string timestamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture);
        for (int suffix = 0; suffix < 1000; suffix++)
        {
            string extra = suffix == 0 ? "" : $"-{suffix}";
            string candidate = Path.Combine(directory, $"{stem}.corrupt-{timestamp}{extra}{extension}");
            if (File.Exists(candidate)) continue;
            try
            {
                File.Move(_storagePath, candidate);
                return candidate;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                return "";
            }
        }
        return "";
    }

    private void WriteTemporary(PlayerSkillProgress progress, Godot.Collections.Array<CatSkillPreset> presets)
    {
        using var stream = new FileStream(TemporaryPath, FileMode.Create, System.IO.FileAccess.Write, FileShare.None,
            4096, FileOptions.WriteThrough);
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", SchemaVersion);
            writer.WritePropertyName("playerSkillProgress");
            writer.WriteStartObject();
            writer.WriteNumber("playerLevel", progress.PlayerLevel);
            writer.WriteNumber("unlockedPoints", progress.UnlockedPoints);
            writer.WritePropertyName("ownedSkillIds");
            writer.WriteStartArray();
            foreach (string id in progress.OwnedSkillIds) writer.WriteStringValue(id);
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WritePropertyName("catSkillPresets");
            writer.WriteStartArray();
            foreach (CatSkillPreset preset in presets)
            {
                writer.WriteStartObject();
                writer.WriteString("characterId", preset.CharacterId);
                writer.WriteString("activeSkillId", preset.ActiveSkillId);
                writer.WritePropertyName("supportSkillIds");
                writer.WriteStartArray();
                foreach (string id in preset.SupportSkillIds) writer.WriteStringValue(id);
                writer.WriteEndArray();
                writer.WriteNumber("allocatedPoints", preset.AllocatedPoints);
                writer.WriteString("updatedAtUtc", preset.UpdatedAtUtc);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.Flush();
        }
        stream.Flush(true);
    }

    private static ParseOutcome ReadDocument(string path)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path));
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Root must be an object.");
            if (!root.TryGetProperty("schemaVersion", out JsonElement versionElement)
                || !TryGetInt32(versionElement, out int version))
                throw new InvalidDataException("schemaVersion must be an integer.");
            if (version > SchemaVersion) return ParseOutcome.Future(version);
            if (version != SchemaVersion) throw new InvalidDataException($"Unsupported schemaVersion {version}.");
            RequireFields(root, "root", "schemaVersion", "playerSkillProgress", "catSkillPresets");

            JsonElement progressJson = RequiredObject(root, "playerSkillProgress", "root");
            RequireFields(progressJson, "playerSkillProgress", "playerLevel", "unlockedPoints", "ownedSkillIds");
            int level = RequiredInt(progressJson, "playerLevel", "playerSkillProgress", 1);
            int points = RequiredInt(progressJson, "unlockedPoints", "playerSkillProgress", 0);
            string[] owned = RequiredStringArray(progressJson, "ownedSkillIds", "playerSkillProgress", false);
            var progress = new PlayerSkillProgress { PlayerLevel = level, UnlockedPoints = points };
            foreach (string id in owned) progress.OwnedSkillIds.Add(id);

            if (!root.TryGetProperty("catSkillPresets", out JsonElement presetsJson)
                || presetsJson.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("root.catSkillPresets must be an array.");
            var presets = new Godot.Collections.Array<CatSkillPreset>();
            var characterIds = new HashSet<string>(StringComparer.Ordinal);
            int index = 0;
            foreach (JsonElement presetJson in presetsJson.EnumerateArray())
            {
                string context = $"catSkillPresets[{index}]";
                if (presetJson.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException($"{context} must be an object.");
                RequireFields(presetJson, context, "characterId", "activeSkillId", "supportSkillIds", "allocatedPoints", "updatedAtUtc");
                string characterId = RequiredString(presetJson, "characterId", context);
                if (!characterIds.Add(characterId))
                    throw new InvalidDataException($"{context}.characterId is duplicated.");
                string activeId = RequiredString(presetJson, "activeSkillId", context);
                string[] supports = RequiredStringArray(presetJson, "supportSkillIds", context, false);
                int allocated = RequiredInt(presetJson, "allocatedPoints", context, 0);
                string updatedAt = RequiredString(presetJson, "updatedAtUtc", context);
                if (!IsUtcTimestamp(updatedAt))
                    throw new InvalidDataException($"{context}.updatedAtUtc must be a UTC timestamp.");
                var preset = new CatSkillPreset
                {
                    CharacterId = characterId,
                    ActiveSkillId = activeId,
                    AllocatedPoints = allocated,
                    UpdatedAtUtc = updatedAt
                };
                foreach (string id in supports) preset.SupportSkillIds.Add(id);
                presets.Add(preset);
                index++;
            }
            return ParseOutcome.Valid(progress, presets);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
                                   or InvalidDataException or NotSupportedException)
        {
            return ParseOutcome.Invalid(ex.Message);
        }
    }

    private static void ValidateModel(PlayerSkillProgress progress, Godot.Collections.Array<CatSkillPreset> presets)
    {
        if (progress is null) throw new InvalidDataException("Player skill progress is required.");
        if (progress.PlayerLevel < 1) throw new InvalidDataException("Player level must be positive.");
        if (progress.UnlockedPoints < 0) throw new InvalidDataException("Unlocked points cannot be negative.");
        ValidateStrings(progress.OwnedSkillIds, "ownedSkillIds");
        var characters = new HashSet<string>(StringComparer.Ordinal);
        foreach (CatSkillPreset? preset in presets)
        {
            if (preset is null) throw new InvalidDataException("A skill preset is missing.");
            ValidateString(preset.CharacterId, "characterId");
            if (!characters.Add(preset.CharacterId)) throw new InvalidDataException("characterId is duplicated.");
            ValidateString(preset.ActiveSkillId, "activeSkillId");
            ValidateStrings(preset.SupportSkillIds, "supportSkillIds");
            if (preset.AllocatedPoints < 0) throw new InvalidDataException("Allocated points cannot be negative.");
            ValidateString(preset.UpdatedAtUtc, "updatedAtUtc");
            if (!IsUtcTimestamp(preset.UpdatedAtUtc))
                throw new InvalidDataException("updatedAtUtc must be a UTC timestamp.");
        }
    }

    private static void RequireFields(JsonElement element, string context, params string[] allowed)
    {
        var names = new HashSet<string>(allowed, StringComparer.Ordinal);
        foreach (JsonProperty property in element.EnumerateObject())
            if (!names.Remove(property.Name))
                throw new InvalidDataException($"{context}.{property.Name} is unknown or duplicated.");
        if (names.Count > 0)
            throw new InvalidDataException($"{context}.{names.Order(StringComparer.Ordinal).First()} is required.");
    }

    private static JsonElement RequiredObject(JsonElement parent, string name, string context)
    {
        if (!parent.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"{context}.{name} must be an object.");
        return value;
    }

    private static int RequiredInt(JsonElement parent, string name, string context, int minimum)
    {
        if (!parent.TryGetProperty(name, out JsonElement value) || !TryGetInt32(value, out int result) || result < minimum)
            throw new InvalidDataException($"{context}.{name} must be an integer of at least {minimum}.");
        return result;
    }

    private static bool TryGetInt32(JsonElement value, out int result)
    {
        result = 0;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out double number)
            || !double.IsFinite(number) || number != Math.Truncate(number)
            || number < int.MinValue || number > int.MaxValue)
            return false;
        result = (int)number;
        return true;
    }

    private static string RequiredString(JsonElement parent, string name, string context)
    {
        if (!parent.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException($"{context}.{name} must be a string.");
        string result = value.GetString() ?? "";
        ValidateString(result, $"{context}.{name}");
        return result;
    }

    private static string[] RequiredStringArray(JsonElement parent, string name, string context, bool rejectDuplicates)
    {
        if (!parent.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"{context}.{name} must be an array.");
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int index = 0;
        foreach (JsonElement item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
                throw new InvalidDataException($"{context}.{name}[{index}] must be a string.");
            string id = item.GetString() ?? "";
            ValidateString(id, $"{context}.{name}[{index}]");
            if (rejectDuplicates && !seen.Add(id))
                throw new InvalidDataException($"{context}.{name}[{index}] is duplicated.");
            result.Add(id);
            index++;
        }
        return result.ToArray();
    }

    private static void ValidateStrings(IEnumerable<string> values, string context)
    {
        int index = 0;
        foreach (string value in values) ValidateString(value, $"{context}[{index++}]");
    }

    private static void ValidateString(string value, string context)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidDataException($"{context} cannot be empty.");
    }

    private static bool IsUtcTimestamp(string value) =>
        (value.EndsWith("Z", StringComparison.OrdinalIgnoreCase) || value.EndsWith("+00:00", StringComparison.Ordinal))
        && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset parsed)
        && parsed.Offset == TimeSpan.Zero;

    private static SkillProfileLoadResult LoadedResult(
        ParseOutcome parsed,
        SkillProfileLoadStatus status,
        string message,
        string preservedPath = "",
        bool requiresExplicitOverwrite = false) => new()
        {
            Status = status,
            Progress = parsed.Progress!,
            Presets = parsed.Presets!,
            Message = message,
            PreservedPath = preservedPath,
            RequiresExplicitOverwrite = requiresExplicitOverwrite
        };

    private SkillProfileLoadResult DefaultsResult(
        PlayerSkillDefaults defaults,
        SkillProfileLoadStatus status,
        string message,
        string preservedPath = "",
        bool requiresExplicitOverwrite = false)
    {
        var progress = new PlayerSkillProgress
        {
            PlayerLevel = defaults.Progress.PlayerLevel,
            UnlockedPoints = defaults.Progress.UnlockedPoints
        };
        foreach (string id in defaults.Progress.OwnedSkillIds) progress.OwnedSkillIds.Add(id);
        var presets = new Godot.Collections.Array<CatSkillPreset>();
        foreach (CatSkillPreset source in defaults.InitialPresets)
        {
            var preset = new CatSkillPreset
            {
                CharacterId = source.CharacterId,
                ActiveSkillId = source.ActiveSkillId,
                AllocatedPoints = source.AllocatedPoints,
                UpdatedAtUtc = source.UpdatedAtUtc
            };
            foreach (string id in source.SupportSkillIds) preset.SupportSkillIds.Add(id);
            presets.Add(preset);
        }
        _requiresExplicitOverwrite = requiresExplicitOverwrite;
        return new SkillProfileLoadResult
        {
            Status = status,
            Progress = progress,
            Presets = presets,
            Message = message,
            PreservedPath = preservedPath,
            RequiresExplicitOverwrite = requiresExplicitOverwrite
        };
    }

    private static SkillProfileSaveResult SaveResult(SkillProfileSaveStatus status, string message) => new()
    {
        Status = status,
        Message = message
    };

    private void TryDeleteTemporary()
    {
        try { if (File.Exists(TemporaryPath)) File.Delete(TemporaryPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException) { }
    }

    private enum ParseKind { Valid, Invalid, Future }

    private sealed record ParseOutcome(
        ParseKind Kind,
        PlayerSkillProgress? Progress,
        Godot.Collections.Array<CatSkillPreset>? Presets,
        string Message,
        int Version)
    {
        public static ParseOutcome Valid(PlayerSkillProgress progress, Godot.Collections.Array<CatSkillPreset> presets) =>
            new(ParseKind.Valid, progress, presets, "", SchemaVersion);
        public static ParseOutcome Invalid(string message) => new(ParseKind.Invalid, null, null, message, 0);
        public static ParseOutcome Future(int version) => new(ParseKind.Future, null, null, "", version);
    }
}
