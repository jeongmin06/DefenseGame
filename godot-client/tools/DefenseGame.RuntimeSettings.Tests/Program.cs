using DefenseGame.Client.Network;

AssertEqual(
    "https://staging.example.com/api",
    ClientRuntimeSettings.ResolveServerBaseUrl(true, " https://staging.example.com/api/ ", "http://project.invalid"),
    "environment server URL wins and is normalized");
AssertEqual(
    "http://127.0.0.1:5080",
    ClientRuntimeSettings.ResolveServerBaseUrl(false, "https://ignored.invalid", "http://127.0.0.1:5080/"),
    "project server URL fallback");
AssertEqual(
    ClientRuntimeSettings.DefaultServerUrl,
    ClientRuntimeSettings.ResolveServerBaseUrl(false, null, null),
    "default server URL fallback");
AssertEqual(
    "https://example.com",
    ClientRuntimeSettings.ResolveServerBaseUrl(true, "HTTPS://example.com/", null),
    "URL scheme is case insensitive");
AssertEqual(
    "remote-player",
    ClientRuntimeSettings.ResolvePlayerId(true, " remote-player ", "project-player"),
    "environment player ID wins and is trimmed");
AssertEqual(
    "project-player",
    ClientRuntimeSettings.ResolvePlayerId(false, null, " project-player "),
    "project player ID fallback");
AssertEqual(
    ClientAuthenticationMode.Guest,
    ClientRuntimeSettings.ResolveAuthenticationMode(true, " GUEST ", "disabled"),
    "environment authentication mode wins and is normalized");
AssertEqual(
    ClientAuthenticationMode.Disabled,
    ClientRuntimeSettings.ResolveAuthenticationMode(false, null, "disabled"),
    "project authentication mode fallback");
AssertEqual(
    "/tmp/defensegame-session.json",
    ClientRuntimeSettings.ResolveGuestSessionPath(true, " /tmp/defensegame-session.json ", "user://ignored.json"),
    "environment guest session path wins and is trimmed");

AssertThrows(() => ClientRuntimeSettings.ResolveServerBaseUrl(true, "", "http://project.invalid"), "blank environment URL");
AssertThrows(() => ClientRuntimeSettings.ResolveServerBaseUrl(true, "ftp://example.com", null), "non-http URL");
AssertThrows(() => ClientRuntimeSettings.ResolveServerBaseUrl(true, "https://user@example.com", null), "URL credentials");
AssertThrows(() => ClientRuntimeSettings.ResolveServerBaseUrl(true, "https://example.com?q=1", null), "URL query");
AssertThrows(() => ClientRuntimeSettings.ResolveServerBaseUrl(true, "https://example.com:abc", null), "invalid URL port");
AssertThrows(() => ClientRuntimeSettings.ResolveServerBaseUrl(true, "http://127.0.0.1:0", null), "zero URL port");
AssertThrows(() => ClientRuntimeSettings.ResolveServerBaseUrl(true, "https://.", null), "invalid URL host");
AssertThrows(() => ClientRuntimeSettings.ResolveServerBaseUrl(true, "http://[::1", null), "invalid IPv6 URL");
AssertThrows(() => ClientRuntimeSettings.ResolvePlayerId(true, "   ", "project-player"), "blank environment player ID");
AssertThrows(() => ClientRuntimeSettings.ResolveAuthenticationMode(true, "token", "disabled"), "invalid authentication mode");
AssertThrows(() => ClientRuntimeSettings.ResolveGuestSessionPath(true, " ", "user://guest_session.json"), "blank guest session path");

string sessionDirectory = Path.Combine(Path.GetTempPath(), "defensegame-guest-session-qa", Guid.NewGuid().ToString("N"));
string sessionPath = Path.Combine(sessionDirectory, "guest_session.json");
try
{
    var store = new GuestSessionStore(sessionPath);
    var nowUtc = new DateTimeOffset(2026, 10, 29, 0, 0, 0, TimeSpan.Zero);
    var session = new GuestSession(
        "https://staging.example.com",
        "guest_test",
        "secret-token-not-logged",
        nowUtc.AddHours(1));
    store.Save(session);

    GuestSessionLoadResult loaded = store.Load("https://staging.example.com", nowUtc);
    AssertEqual(GuestSessionLoadStatus.Valid, loaded.Status, "guest session round trip status");
    AssertEqual("guest_test", loaded.Session?.PlayerId, "guest session round trip player");
    AssertTrue(!File.Exists(sessionPath + ".tmp"), "guest session atomic temporary file cleanup");
    AssertEqual(
        GuestSessionLoadStatus.Missing,
        store.Load("https://other.example.com", nowUtc).Status,
        "guest session server isolation");
    AssertEqual(
        GuestSessionLoadStatus.Expired,
        store.Load("https://staging.example.com", nowUtc.AddHours(2)).Status,
        "guest session expiry");
}
finally
{
    if (Directory.Exists(sessionDirectory)) Directory.Delete(sessionDirectory, recursive: true);
}

Console.WriteLine("RUNTIME SETTINGS QA PASS");

static void AssertEqual<T>(T expected, T actual, string name)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"{name}: expected '{expected}', actual '{actual}'");
}

static void AssertTrue(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException($"{name}: expected true");
}

static void AssertThrows(Action action, string name)
{
    try
    {
        action();
    }
    catch (InvalidOperationException)
    {
        return;
    }

    throw new InvalidOperationException($"{name}: expected InvalidOperationException");
}
