using System.Text.Json;
using DefenseGame.Server.Domain;
using DefenseGame.Server.Services;

namespace DefenseGame.Server.Stores;

public sealed class JsonFileProgressStore : IProgressStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _filePath;
    private readonly ISystemClock _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonFileProgressStore(string filePath, ISystemClock clock)
    {
        _filePath = filePath;
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
            var json = await File.ReadAllTextAsync(_filePath, cancellationToken);
            return JsonSerializer.Deserialize<ProgressDatabase>(json, JsonOptions) ?? new ProgressDatabase();
        }
        catch (FileNotFoundException)
        {
            return new ProgressDatabase();
        }
    }

    private Task WriteDatabaseAsync(ProgressDatabase db, CancellationToken cancellationToken)
    {
        return File.WriteAllTextAsync(_filePath, JsonSerializer.Serialize(db, JsonOptions), cancellationToken);
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
        public Dictionary<string, ProgressEntry> Users { get; init; } = new(StringComparer.Ordinal);
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
