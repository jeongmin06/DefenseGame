using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DefenseGame.Client.Network;

public sealed class StageSquadServerClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<StageSquadServerResult> LoadAsync(
        string baseUrl,
        string userId,
        string stageId,
        CancellationToken cancellationToken)
    {
        try
        {
            string url = $"{NormalizeBaseUrl(baseUrl)}/v1/squads/{Uri.EscapeDataString(userId)}/{Uri.EscapeDataString(stageId)}";
            using HttpResponseMessage response = await Http.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode) return await Failure(response, cancellationToken);
            StageSquadServerPayload? payload = await response.Content.ReadFromJsonAsync<StageSquadServerPayload>(JsonOptions, cancellationToken);
            return payload is null
                ? StageSquadServerResult.Failed("invalid_response", "Server returned an empty squad response.")
                : StageSquadServerResult.Succeeded(payload);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or UriFormatException)
        {
            return StageSquadServerResult.Failed("server_unavailable", ex.Message);
        }
    }

    public async Task<StageSquadServerResult> SaveAsync(
        string baseUrl,
        string userId,
        string stageId,
        string[] characterIds,
        int expectedRevision,
        CancellationToken cancellationToken)
    {
        try
        {
            string url = $"{NormalizeBaseUrl(baseUrl)}/v1/squads/{Uri.EscapeDataString(stageId)}";
            var request = new StageSquadServerSaveRequest(userId, characterIds, expectedRevision);
            using HttpResponseMessage response = await Http.PutAsJsonAsync(url, request, JsonOptions, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                StageSquadServerPayload? payload = await response.Content.ReadFromJsonAsync<StageSquadServerPayload>(JsonOptions, cancellationToken);
                return payload is null
                    ? StageSquadServerResult.Failed("invalid_response", "Server returned an empty squad response.")
                    : StageSquadServerResult.Succeeded(payload);
            }
            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                StageSquadServerError? conflict = await response.Content.ReadFromJsonAsync<StageSquadServerError>(JsonOptions, cancellationToken);
                return StageSquadServerResult.Conflicted(conflict?.Current, conflict?.Message ?? "Squad revision conflict.");
            }
            return await Failure(response, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or UriFormatException)
        {
            return StageSquadServerResult.Failed("server_unavailable", ex.Message);
        }
    }

    private static async Task<StageSquadServerResult> Failure(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        StageSquadServerError? error = await response.Content.ReadFromJsonAsync<StageSquadServerError>(JsonOptions, cancellationToken);
        return StageSquadServerResult.Failed(
            error?.ErrorCode ?? $"http_{(int)response.StatusCode}",
            error?.Message ?? response.ReasonPhrase ?? "Server request failed.");
    }

    private static string NormalizeBaseUrl(string baseUrl) => baseUrl.Trim().TrimEnd('/');

    private sealed record StageSquadServerSaveRequest(string UserId, string[] CharacterIds, int ExpectedRevision);
    private sealed record StageSquadServerError(string? ErrorCode, string? Message, StageSquadServerPayload? Current);
}

public sealed record StageSquadServerPayload(
    string UserId,
    string StageId,
    string[] CharacterIds,
    int Revision,
    DateTimeOffset? UpdatedAtUtc);

public sealed record StageSquadServerResult(
    bool IsSuccess,
    bool IsConflict,
    string ErrorCode,
    string Message,
    StageSquadServerPayload? Payload)
{
    public static StageSquadServerResult Succeeded(StageSquadServerPayload payload) =>
        new(true, false, "ok", "", payload);

    public static StageSquadServerResult Failed(string errorCode, string message) =>
        new(false, false, errorCode, message, null);

    public static StageSquadServerResult Conflicted(StageSquadServerPayload? current, string message) =>
        new(false, true, "revision_conflict", message, current);
}
