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

AssertThrows(() => ClientRuntimeSettings.ResolveServerBaseUrl(true, "", "http://project.invalid"), "blank environment URL");
AssertThrows(() => ClientRuntimeSettings.ResolveServerBaseUrl(true, "ftp://example.com", null), "non-http URL");
AssertThrows(() => ClientRuntimeSettings.ResolveServerBaseUrl(true, "https://user@example.com", null), "URL credentials");
AssertThrows(() => ClientRuntimeSettings.ResolveServerBaseUrl(true, "https://example.com?q=1", null), "URL query");
AssertThrows(() => ClientRuntimeSettings.ResolveServerBaseUrl(true, "https://example.com:abc", null), "invalid URL port");
AssertThrows(() => ClientRuntimeSettings.ResolveServerBaseUrl(true, "http://127.0.0.1:0", null), "zero URL port");
AssertThrows(() => ClientRuntimeSettings.ResolveServerBaseUrl(true, "https://.", null), "invalid URL host");
AssertThrows(() => ClientRuntimeSettings.ResolveServerBaseUrl(true, "http://[::1", null), "invalid IPv6 URL");
AssertThrows(() => ClientRuntimeSettings.ResolvePlayerId(true, "   ", "project-player"), "blank environment player ID");

Console.WriteLine("RUNTIME SETTINGS QA PASS");

static void AssertEqual(string expected, string actual, string name)
{
    if (!string.Equals(expected, actual, StringComparison.Ordinal))
        throw new InvalidOperationException($"{name}: expected '{expected}', actual '{actual}'");
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
