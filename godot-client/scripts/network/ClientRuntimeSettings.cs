using Godot;
using System;

namespace DefenseGame.Client.Network;

public static class ClientRuntimeSettings
{
    public const string ServerUrlEnvironmentVariable = "DEFENSEGAME_SERVER_URL";
    public const string PlayerIdEnvironmentVariable = "DEFENSEGAME_PLAYER_ID";
    public const string ServerUrlProjectSetting = "defense_game/server/base_url";
    public const string PlayerIdProjectSetting = "defense_game/player/development_user_id";
    public const string DefaultServerUrl = "http://127.0.0.1:5080";
    public const string DefaultPlayerId = "local-development-user";

    public static string ServerBaseUrl() => ResolveServerBaseUrl(
        OS.HasEnvironment(ServerUrlEnvironmentVariable),
        OS.GetEnvironment(ServerUrlEnvironmentVariable),
        (string)ProjectSettings.GetSetting(ServerUrlProjectSetting, DefaultServerUrl));

    public static string PlayerId() => ResolvePlayerId(
        OS.HasEnvironment(PlayerIdEnvironmentVariable),
        OS.GetEnvironment(PlayerIdEnvironmentVariable),
        (string)ProjectSettings.GetSetting(PlayerIdProjectSetting, DefaultPlayerId));

    public static bool TryGet(out ClientRuntimeConfiguration configuration, out string error)
    {
        try
        {
            configuration = new ClientRuntimeConfiguration(ServerBaseUrl(), PlayerId());
            error = "";
            return true;
        }
        catch (InvalidOperationException ex)
        {
            configuration = default;
            error = ex.Message;
            return false;
        }
    }

    public static string ResolveServerBaseUrl(
        bool environmentIsSet,
        string? environmentValue,
        string? projectValue)
    {
        string value = SelectValue(environmentIsSet, environmentValue, projectValue, DefaultServerUrl).Trim();
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            uri.Port == 0 ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidOperationException(
                $"{ServerUrlEnvironmentVariable} must be an absolute http(s) URL without credentials, query, or fragment.");
        }

        return uri.AbsoluteUri.TrimEnd('/');
    }

    public static string ResolvePlayerId(
        bool environmentIsSet,
        string? environmentValue,
        string? projectValue)
    {
        string value = SelectValue(environmentIsSet, environmentValue, projectValue, DefaultPlayerId).Trim();
        if (value.Length == 0)
        {
            throw new InvalidOperationException($"{PlayerIdEnvironmentVariable} must not be empty.");
        }

        return value;
    }

    private static string SelectValue(
        bool environmentIsSet,
        string? environmentValue,
        string? projectValue,
        string fallback)
    {
        if (environmentIsSet) return environmentValue ?? "";
        return projectValue ?? fallback;
    }
}

public readonly record struct ClientRuntimeConfiguration(string ServerBaseUrl, string PlayerId);
