using System.Text.Json;
using System.Text.Json.Serialization;
using DefenseGame.Server.Domain;
using DefenseGame.Server.Services;

namespace DefenseGame.Server.Stores;

public sealed class JsonFileProgressStore : IProgressStore
{
    private const int LegacySchemaVersion = 0;
    private const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _filePath;
    private readonly string _temporaryFilePath;
    private readonly string _backupFilePath;
    private readonly ISystemClock _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonFileProgressStore(string filePath, ISystemClock clock)
    {
        _filePath = filePath;
        _temporaryFilePath = $"{filePath}.tmp";
        _backupFilePath = $"{filePath}.bak";
        _clock = clock;
        // Directory setup happens when the store is constructed, outside actor messages.
        var directoryPath = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
            Directory.CreateDirectory(directoryPath);
    }

    public async Task<UserProgress> GetOrCreateAsync(string userId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var db = await ReadDatabaseAsync(cancellationToken);
            if (!db.Users.TryGetValue(userId, out var entry))
            {
                var newProgress = UserProgress.New(userId, _clock.UtcNow);
                db.Users[userId] = ToEntry(newProgress);
                await WriteDatabaseAsync(db, cancellationToken);
                return newProgress.Clone();
            }

            return FromEntry(userId, entry);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<UserProgress> UpsertAsync(UserProgress progress, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var db = await ReadDatabaseAsync(cancellationToken);
            db.Users[progress.UserId] = ToEntry(progress);
            await WriteDatabaseAsync(db, cancellationToken);
            return progress.Clone();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ProgressDatabase> ReadDatabaseAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await ReadDatabaseFileAsync(_filePath, cancellationToken);
        }
        catch (FileNotFoundException)
        {
            return await RecoverBackupOrCreateAsync(cancellationToken);
        }
        catch (JsonException primaryError)
        {
            return await RecoverBackupAsync(primaryError, cancellationToken);
        }
    }

    private async Task<ProgressDatabase> ReadDatabaseFileAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        var json = await File.ReadAllTextAsync(filePath, cancellationToken);
        ProgressDatabase db = JsonSerializer.Deserialize<ProgressDatabase>(json, JsonOptions)
            ?? throw new JsonException($"Progress database '{filePath}' contains a null document.");
        if (db.Users is null)
            throw new JsonException($"Progress database '{filePath}' contains a null users object.");

        if (db.SchemaVersion == LegacySchemaVersion)
            db.SchemaVersion = CurrentSchemaVersion;
        else if (db.SchemaVersion < LegacySchemaVersion)
            throw new JsonException(
                $"Progress database '{filePath}' contains invalid schema version {db.SchemaVersion}.");
        else if (db.SchemaVersion > CurrentSchemaVersion)
            throw new InvalidDataException(
                $"Progress database '{filePath}' uses unsupported schema version {db.SchemaVersion}. " +
                $"This server supports version {CurrentSchemaVersion}.");

        ValidateDatabaseStructure(db, filePath);
        return db;
    }

    private static void ValidateDatabaseStructure(ProgressDatabase db, string filePath)
    {
        foreach ((string userId, ProgressEntry entry) in db.Users)
        {
            if (string.IsNullOrWhiteSpace(userId) || entry is null || entry.ClearedStageIds is null ||
                entry.OwnedCharacterIds is null || entry.OwnedCharacterIds.Any(string.IsNullOrWhiteSpace) ||
                entry.StageSquads is null)
            {
                throw new JsonException(
                    $"Progress database '{filePath}' contains an invalid entry for user '{userId}'.");
            }

            if (entry.StageSquads.Any(pair =>
                    string.IsNullOrWhiteSpace(pair.Key) || pair.Value is null ||
                    pair.Value.CharacterIds is null ||
                    pair.Value.CharacterIds.Any(string.IsNullOrWhiteSpace)))
            {
                throw new JsonException(
                    $"Progress database '{filePath}' contains an invalid squad for user '{userId}'.");
            }

            SkillProfileEntry? profile = entry.SkillProfile;
            if (profile is null)
                continue;

            if (profile.OwnedSkillIds is null || profile.OwnedSkillIds.Any(string.IsNullOrWhiteSpace) ||
                profile.Presets is null || profile.Presets.Any(pair =>
                    string.IsNullOrWhiteSpace(pair.Key) || pair.Value is null ||
                    string.IsNullOrWhiteSpace(pair.Value.ActiveSkillId) ||
                    pair.Value.SupportSkillIds is null ||
                    pair.Value.SupportSkillIds.Any(string.IsNullOrWhiteSpace)))
            {
                throw new JsonException(
                    $"Progress database '{filePath}' contains an invalid skill profile for user '{userId}'.");
            }
        }
    }

    private async Task<ProgressDatabase> RecoverBackupOrCreateAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await RestoreBackupAsync(cancellationToken);
        }
        catch (FileNotFoundException)
        {
            return ProgressDatabase.CreateCurrent();
        }
        catch (JsonException backupError)
        {
            throw new InvalidDataException(
                $"Progress database '{_filePath}' is missing and backup '{_backupFilePath}' is corrupted.",
                backupError);
        }
    }

    private async Task<ProgressDatabase> RecoverBackupAsync(
        JsonException primaryError,
        CancellationToken cancellationToken)
    {
        try
        {
            return await RestoreBackupAsync(cancellationToken);
        }
        catch (FileNotFoundException)
        {
            throw new InvalidDataException(
                $"Progress database '{_filePath}' is corrupted and no backup is available.",
                primaryError);
        }
        catch (JsonException backupError)
        {
            throw new InvalidDataException(
                $"Progress database '{_filePath}' and backup '{_backupFilePath}' are both corrupted.",
                new AggregateException(primaryError, backupError));
        }
    }

    private async Task<ProgressDatabase> RestoreBackupAsync(CancellationToken cancellationToken)
    {
        ProgressDatabase db = await ReadDatabaseFileAsync(_backupFilePath, cancellationToken);
        try
        {
            File.Copy(_backupFilePath, _temporaryFilePath, overwrite: true);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(_temporaryFilePath, _filePath, overwrite: true);
            return db;
        }
        finally
        {
            DeleteTemporaryFileBestEffort();
        }
    }

    private async Task WriteDatabaseAsync(ProgressDatabase db, CancellationToken cancellationToken)
    {
        try
        {
            await File.WriteAllTextAsync(
                _temporaryFilePath,
                JsonSerializer.Serialize(db, JsonOptions),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            if (File.Exists(_filePath))
                File.Replace(_temporaryFilePath, _filePath, _backupFilePath, ignoreMetadataErrors: true);
            else
                File.Move(_temporaryFilePath, _filePath);
        }
        finally
        {
            DeleteTemporaryFileBestEffort();
        }
    }

    private void DeleteTemporaryFileBestEffort()
    {
        try
        {
            File.Delete(_temporaryFilePath);
        }
        catch (IOException)
        {
            // Preserve the original write or recovery failure when cleanup is blocked.
        }
        catch (UnauthorizedAccessException)
        {
            // Preserve the original write or recovery failure when cleanup is blocked.
        }
    }

    private static ProgressEntry ToEntry(UserProgress progress)
    {
        return new ProgressEntry
        {
            ClearedStageIds = progress.ClearedStageIds.OrderBy(x => x).ToList(),
            OwnedCharacterIds = progress.OwnedCharacterIds.Order(StringComparer.Ordinal).ToList(),
            StageSquads = progress.StageSquads.ToDictionary(
                pair => pair.Key,
                pair => new StageSquadEntry
                {
                    CharacterIds = new List<string>(pair.Value.CharacterIds),
                    Revision = pair.Value.Revision,
                    UpdatedAtUtc = pair.Value.UpdatedAtUtc
                },
                StringComparer.Ordinal),
            SkillProfile = new SkillProfileEntry
            {
                PlayerLevel = progress.SkillProfile.PlayerLevel,
                UnlockedPoints = progress.SkillProfile.UnlockedPoints,
                OwnedSkillIds = progress.SkillProfile.OwnedSkillIds.Order(StringComparer.Ordinal).ToList(),
                Presets = progress.SkillProfile.Presets.ToDictionary(
                    pair => pair.Key,
                    pair => new SkillPresetEntry
                    {
                        ActiveSkillId = pair.Value.ActiveSkillId,
                        SupportSkillIds = new List<string>(pair.Value.SupportSkillIds),
                        AllocatedPoints = pair.Value.AllocatedPoints,
                        UpdatedAtUtc = pair.Value.UpdatedAtUtc
                    },
                    StringComparer.Ordinal),
                Revision = progress.SkillProfile.Revision,
                UpdatedAtUtc = progress.SkillProfile.UpdatedAtUtc
            },
            UpdatedAtUtc = progress.UpdatedAtUtc
        };
    }

    private UserProgress FromEntry(string userId, ProgressEntry entry)
    {
        DateTimeOffset fallbackTime = entry.UpdatedAtUtc == default ? _clock.UtcNow : entry.UpdatedAtUtc;
        SkillProfileState skillProfile = entry.SkillProfile is null
            ? PlayerDefaults.CreateInitialSkillProfile(fallbackTime)
            : new SkillProfileState
            {
                PlayerLevel = entry.SkillProfile.PlayerLevel,
                UnlockedPoints = entry.SkillProfile.UnlockedPoints,
                OwnedSkillIds = entry.SkillProfile.OwnedSkillIds.Count == 0
                    ? new HashSet<string>(PlayerDefaults.InitialSkillIds, StringComparer.Ordinal)
                    : new HashSet<string>(entry.SkillProfile.OwnedSkillIds, StringComparer.Ordinal),
                Presets = entry.SkillProfile.Presets.ToDictionary(
                    pair => pair.Key,
                    pair => new SkillPresetState
                    {
                        CharacterId = pair.Key,
                        ActiveSkillId = pair.Value.ActiveSkillId,
                        SupportSkillIds = new List<string>(pair.Value.SupportSkillIds),
                        AllocatedPoints = pair.Value.AllocatedPoints,
                        UpdatedAtUtc = pair.Value.UpdatedAtUtc
                    },
                    StringComparer.Ordinal),
                Revision = entry.SkillProfile.Revision,
                UpdatedAtUtc = entry.SkillProfile.UpdatedAtUtc
            };
        return new UserProgress
        {
            UserId = userId,
            ClearedStageIds = entry.ClearedStageIds.ToHashSet(),
            OwnedCharacterIds = entry.OwnedCharacterIds.Count == 0
                ? new HashSet<string>(PlayerDefaults.InitialCharacterIds, StringComparer.Ordinal)
                : new HashSet<string>(entry.OwnedCharacterIds, StringComparer.Ordinal),
            StageSquads = entry.StageSquads.ToDictionary(
                pair => pair.Key,
                pair => new StageSquadState
                {
                    StageId = pair.Key,
                    CharacterIds = new List<string>(pair.Value.CharacterIds),
                    Revision = pair.Value.Revision,
                    UpdatedAtUtc = pair.Value.UpdatedAtUtc
                },
                StringComparer.Ordinal),
            SkillProfile = skillProfile,
            UpdatedAtUtc = entry.UpdatedAtUtc
        };
    }

    private sealed class ProgressDatabase
    {
        public int SchemaVersion { get; set; } = LegacySchemaVersion;

        [JsonRequired]
        public Dictionary<string, ProgressEntry> Users { get; init; } = new(StringComparer.Ordinal);

        public static ProgressDatabase CreateCurrent()
        {
            return new ProgressDatabase { SchemaVersion = CurrentSchemaVersion };
        }
    }

    private sealed class ProgressEntry
    {
        public List<int> ClearedStageIds { get; init; } = new();
        public List<string> OwnedCharacterIds { get; init; } = new();
        public Dictionary<string, StageSquadEntry> StageSquads { get; init; } = new(StringComparer.Ordinal);
        public SkillProfileEntry? SkillProfile { get; init; }
        public DateTimeOffset UpdatedAtUtc { get; init; }
    }

    private sealed class StageSquadEntry
    {
        public List<string> CharacterIds { get; init; } = new();
        public int Revision { get; init; }
        public DateTimeOffset UpdatedAtUtc { get; init; }
    }

    private sealed class SkillProfileEntry
    {
        public int PlayerLevel { get; init; }
        public int UnlockedPoints { get; init; }
        public List<string> OwnedSkillIds { get; init; } = new();
        public Dictionary<string, SkillPresetEntry> Presets { get; init; } = new(StringComparer.Ordinal);
        public int Revision { get; init; }
        public DateTimeOffset UpdatedAtUtc { get; init; }
    }

    private sealed class SkillPresetEntry
    {
        public string ActiveSkillId { get; init; } = "";
        public List<string> SupportSkillIds { get; init; } = new();
        public int AllocatedPoints { get; init; }
        public DateTimeOffset UpdatedAtUtc { get; init; }
    }
}
