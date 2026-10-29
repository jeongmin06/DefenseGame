using System;
using System.IO;
using System.Text.Json;

namespace DefenseGame.Client.Network;

public sealed class GuestSessionStore
{
    public const int CurrentSchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    private readonly string _storagePath;

    public GuestSessionStore(string storagePath)
    {
        if (string.IsNullOrWhiteSpace(storagePath))
            throw new ArgumentException("Guest session storage path is required.", nameof(storagePath));
        _storagePath = Path.GetFullPath(storagePath);
    }

    public GuestSessionLoadResult Load(string serverBaseUrl, DateTimeOffset nowUtc)
    {
        if (!File.Exists(_storagePath)) return GuestSessionLoadResult.Missing();

        try
        {
            GuestSessionDocument? document = JsonSerializer.Deserialize<GuestSessionDocument>(
                File.ReadAllText(_storagePath),
                JsonOptions);
            if (document is null ||
                document.SchemaVersion != CurrentSchemaVersion ||
                string.IsNullOrWhiteSpace(document.PlayerId) ||
                string.IsNullOrWhiteSpace(document.AccessToken))
            {
                return GuestSessionLoadResult.Invalid();
            }

            if (!string.Equals(document.ServerBaseUrl, serverBaseUrl, StringComparison.Ordinal))
                return GuestSessionLoadResult.Missing();

            var session = new GuestSession(
                document.ServerBaseUrl,
                document.PlayerId,
                document.AccessToken,
                document.ExpiresAtUtc);
            return document.ExpiresAtUtc <= nowUtc
                ? GuestSessionLoadResult.Expired(session)
                : GuestSessionLoadResult.Valid(session);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return GuestSessionLoadResult.Invalid();
        }
    }

    public void Save(GuestSession session)
    {
        string? directory = Path.GetDirectoryName(_storagePath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        string temporaryPath = $"{_storagePath}.tmp";
        var document = new GuestSessionDocument(
            CurrentSchemaVersion,
            session.ServerBaseUrl,
            session.PlayerId,
            session.AccessToken,
            session.ExpiresAtUtc);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);

        using (var stream = new FileStream(
            temporaryPath,
            FileMode.Create,
            System.IO.FileAccess.Write,
            FileShare.None,
            4096,
            FileOptions.WriteThrough))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }

        RestrictPermissions(temporaryPath);
        File.Move(temporaryPath, _storagePath, overwrite: true);
        RestrictPermissions(_storagePath);
    }

    private static void RestrictPermissions(string path)
    {
        if (OperatingSystem.IsWindows()) return;

        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (Exception exception) when (exception is PlatformNotSupportedException or UnauthorizedAccessException or IOException)
        {
            // The app sandbox still isolates user:// when the filesystem cannot apply Unix modes.
        }
    }

    private sealed record GuestSessionDocument(
        int SchemaVersion,
        string ServerBaseUrl,
        string PlayerId,
        string AccessToken,
        DateTimeOffset ExpiresAtUtc);
}

public sealed record GuestSession(
    string ServerBaseUrl,
    string PlayerId,
    string AccessToken,
    DateTimeOffset ExpiresAtUtc);

public enum GuestSessionLoadStatus
{
    Missing,
    Valid,
    Expired,
    Invalid
}

public sealed record GuestSessionLoadResult(
    GuestSessionLoadStatus Status,
    GuestSession? Session)
{
    public static GuestSessionLoadResult Missing() => new(GuestSessionLoadStatus.Missing, null);
    public static GuestSessionLoadResult Valid(GuestSession session) => new(GuestSessionLoadStatus.Valid, session);
    public static GuestSessionLoadResult Expired(GuestSession session) => new(GuestSessionLoadStatus.Expired, session);
    public static GuestSessionLoadResult Invalid() => new(GuestSessionLoadStatus.Invalid, null);
}
