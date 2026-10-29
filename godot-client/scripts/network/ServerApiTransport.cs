using Godot;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DefenseGame.Client.Network;

public static class ServerApiTransport
{
    private static readonly TimeSpan RefreshWindow = TimeSpan.FromHours(24);
    private static readonly System.Net.Http.HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };
    private static readonly SemaphoreSlim SessionLock = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static GuestSession? _cachedGuestSession;

    public static async Task<HttpResponseMessage> SendAsync(
        ClientRuntimeConfiguration configuration,
        HttpMethod method,
        string relativePath,
        object? body,
        JsonSerializerOptions serializerOptions,
        CancellationToken cancellationToken)
    {
        ServerRequestIdentity identity = await ResolveIdentityAsync(configuration, cancellationToken);
        return await SendAsync(
            configuration,
            identity,
            method,
            relativePath,
            body,
            serializerOptions,
            cancellationToken);
    }

    public static async Task<HttpResponseMessage> SendAsync(
        ClientRuntimeConfiguration configuration,
        ServerRequestIdentity identity,
        HttpMethod method,
        string relativePath,
        object? body,
        JsonSerializerOptions serializerOptions,
        CancellationToken cancellationToken)
    {
        string url = $"{configuration.ServerBaseUrl}{relativePath}";
        using var request = new HttpRequestMessage(method, url);
        if (body is not null)
            request.Content = JsonContent.Create(body, options: serializerOptions);
        if (!string.IsNullOrEmpty(identity.AccessToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", identity.AccessToken);

        return await Http.SendAsync(request, cancellationToken);
    }

    public static async Task<ServerRequestIdentity> ResolveIdentityAsync(
        ClientRuntimeConfiguration configuration,
        CancellationToken cancellationToken)
    {
        if (configuration.AuthenticationMode == ClientAuthenticationMode.Disabled)
            return new ServerRequestIdentity(configuration.PlayerId, null);

        await SessionLock.WaitAsync(cancellationToken);
        try
        {
            DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
            string storagePath = configuration.GuestSessionPath.StartsWith("user://", StringComparison.Ordinal)
                ? ProjectSettings.GlobalizePath(configuration.GuestSessionPath)
                : configuration.GuestSessionPath;
            var store = new GuestSessionStore(storagePath);
            if (_cachedGuestSession is not null &&
                string.Equals(_cachedGuestSession.ServerBaseUrl, configuration.ServerBaseUrl, StringComparison.Ordinal))
            {
                bool shouldRefresh = _cachedGuestSession.ExpiresAtUtc <= nowUtc.Add(RefreshWindow);
                GuestSession cached = await ResolveExistingSessionAsync(
                    configuration.ServerBaseUrl,
                    _cachedGuestSession,
                    nowUtc,
                    cancellationToken);
                if (shouldRefresh) store.Save(cached);
                _cachedGuestSession = cached;
                return new ServerRequestIdentity(cached.PlayerId, cached.AccessToken);
            }

            GuestSessionLoadResult loaded = store.Load(configuration.ServerBaseUrl, nowUtc);
            if (loaded.Status == GuestSessionLoadStatus.Expired)
            {
                throw new ServerAuthenticationException(
                    "guest_session_expired",
                    "The saved guest session has expired and cannot be recovered automatically.");
            }
            if (loaded.Status == GuestSessionLoadStatus.Invalid)
            {
                throw new ServerAuthenticationException(
                    "invalid_guest_session",
                    "The saved guest session is invalid and was not replaced automatically.");
            }
            if (loaded.Status == GuestSessionLoadStatus.Valid)
            {
                GuestSession stored = await ResolveExistingSessionAsync(
                    configuration.ServerBaseUrl,
                    loaded.Session!,
                    nowUtc,
                    cancellationToken);
                store.Save(stored);
                _cachedGuestSession = stored;
                return new ServerRequestIdentity(stored.PlayerId, stored.AccessToken);
            }

            GuestSession issued = await IssueGuestSessionAsync(configuration.ServerBaseUrl, cancellationToken);
            store.Save(issued);
            _cachedGuestSession = issued;
            return new ServerRequestIdentity(issued.PlayerId, issued.AccessToken);
        }
        finally
        {
            SessionLock.Release();
        }
    }

    private static async Task<GuestSession> ResolveExistingSessionAsync(
        string serverBaseUrl,
        GuestSession session,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (session.ExpiresAtUtc <= nowUtc)
        {
            throw new ServerAuthenticationException(
                "guest_session_expired",
                "The saved guest session has expired and cannot be recovered automatically.");
        }

        return session.ExpiresAtUtc > nowUtc.Add(RefreshWindow)
            ? session
            : await RefreshGuestSessionAsync(serverBaseUrl, session, cancellationToken);
    }

    private static async Task<GuestSession> IssueGuestSessionAsync(
        string serverBaseUrl,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{serverBaseUrl}/v1/auth/guest");
        using HttpResponseMessage response = await Http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            GuestLoginError? error = await response.Content
                .ReadFromJsonAsync<GuestLoginError>(JsonOptions, cancellationToken);
            throw new ServerAuthenticationException(
                error?.ErrorCode ?? $"http_{(int)response.StatusCode}",
                error?.Message ?? response.ReasonPhrase ?? "Guest login failed.");
        }

        GuestLoginPayload? payload = await response.Content
            .ReadFromJsonAsync<GuestLoginPayload>(JsonOptions, cancellationToken);
        if (payload is null ||
            string.IsNullOrWhiteSpace(payload.PlayerId) ||
            string.IsNullOrWhiteSpace(payload.AccessToken) ||
            payload.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            throw new ServerAuthenticationException(
                "invalid_authentication_response",
                "Server returned an invalid guest login response.");
        }

        return new GuestSession(
            serverBaseUrl,
            payload.PlayerId,
            payload.AccessToken,
            payload.ExpiresAtUtc);
    }

    private static async Task<GuestSession> RefreshGuestSessionAsync(
        string serverBaseUrl,
        GuestSession session,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{serverBaseUrl}/v1/auth/refresh");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        using HttpResponseMessage response = await Http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            GuestLoginError? error = await response.Content
                .ReadFromJsonAsync<GuestLoginError>(JsonOptions, cancellationToken);
            throw new ServerAuthenticationException(
                error?.ErrorCode ?? $"http_{(int)response.StatusCode}",
                error?.Message ?? response.ReasonPhrase ?? "Guest token refresh failed.");
        }

        GuestLoginPayload? payload = await response.Content
            .ReadFromJsonAsync<GuestLoginPayload>(JsonOptions, cancellationToken);
        if (payload is null ||
            !string.Equals(payload.PlayerId, session.PlayerId, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(payload.AccessToken) ||
            payload.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            throw new ServerAuthenticationException(
                "invalid_authentication_response",
                "Server returned an invalid guest refresh response.");
        }

        return new GuestSession(
            serverBaseUrl,
            payload.PlayerId,
            payload.AccessToken,
            payload.ExpiresAtUtc);
    }

    private sealed record GuestLoginPayload(
        string PlayerId,
        string AccessToken,
        DateTimeOffset ExpiresAtUtc);

    private sealed record GuestLoginError(string? ErrorCode, string? Message);
}

public readonly record struct ServerRequestIdentity(string PlayerId, string? AccessToken);

public sealed class ServerAuthenticationException : Exception
{
    public ServerAuthenticationException(string errorCode, string message) : base(message)
    {
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }
}
