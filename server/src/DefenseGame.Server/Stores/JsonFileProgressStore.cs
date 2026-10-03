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
            UpdatedAtUtc = progress.UpdatedAtUtc
        };
    }

    private static UserProgress FromEntry(string userId, ProgressEntry entry)
    {
        return new UserProgress
        {
            UserId = userId,
            ClearedStageIds = entry.ClearedStageIds.ToHashSet(),
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
        public DateTimeOffset UpdatedAtUtc { get; init; }
    }
}
