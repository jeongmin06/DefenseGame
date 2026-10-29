using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DefenseGame.Client.Network;

public sealed class StageProgressServerClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<StageProgressLoadResult> LoadAsync(
        ClientRuntimeConfiguration configuration,
        CancellationToken cancellationToken)
    {
        try
        {
            ServerRequestIdentity identity = await ServerApiTransport.ResolveIdentityAsync(
                configuration,
                cancellationToken);
            string path = $"/v1/progress/{Uri.EscapeDataString(identity.PlayerId)}";
            using HttpResponseMessage response = await ServerApiTransport.SendAsync(
                configuration,
                identity,
                HttpMethod.Get,
                path,
                body: null,
                JsonOptions,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                StageProgressServerError? error = await ReadError(response, cancellationToken);
                return StageProgressLoadResult.Failed(
                    error?.ErrorCode ?? $"http_{(int)response.StatusCode}",
                    error?.Message ?? response.ReasonPhrase ?? "Server request failed.");
            }
            StageProgressServerPayload? payload = await response.Content
                .ReadFromJsonAsync<StageProgressServerPayload>(JsonOptions, cancellationToken);
            return payload is null
                ? StageProgressLoadResult.Failed("invalid_response", "Server returned an empty progress response.")
                : StageProgressLoadResult.Succeeded(payload);
        }
        catch (ServerAuthenticationException ex)
        {
            return StageProgressLoadResult.Failed(ex.ErrorCode, ex.Message);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or UriFormatException)
        {
            return StageProgressLoadResult.Failed("server_unavailable", ex.Message);
        }
    }

    public async Task<StageResultSubmitResult> SubmitResultAsync(
        ClientRuntimeConfiguration configuration,
        int stageId,
        bool wasVictory,
        CancellationToken cancellationToken)
    {
        try
        {
            ServerRequestIdentity identity = await ServerApiTransport.ResolveIdentityAsync(
                configuration,
                cancellationToken);
            var request = new StageResultSubmitRequest(identity.PlayerId, stageId, wasVictory);
            using HttpResponseMessage response = await ServerApiTransport.SendAsync(
                configuration,
                identity,
                HttpMethod.Post,
                "/v1/stage-clear",
                request,
                JsonOptions,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                StageProgressServerError? error = await ReadError(response, cancellationToken);
                return StageResultSubmitResult.Failed(
                    error?.ErrorCode ?? $"http_{(int)response.StatusCode}",
                    error?.Message ?? response.ReasonPhrase ?? "Server request failed.");
            }
            StageResultServerPayload? payload = await response.Content
                .ReadFromJsonAsync<StageResultServerPayload>(JsonOptions, cancellationToken);
            return payload is null
                ? StageResultSubmitResult.Failed("invalid_response", "Server returned an empty result response.")
                : StageResultSubmitResult.Succeeded(payload);
        }
        catch (ServerAuthenticationException ex)
        {
            return StageResultSubmitResult.Failed(ex.ErrorCode, ex.Message);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or UriFormatException)
        {
            return StageResultSubmitResult.Failed("server_unavailable", ex.Message);
        }
    }

    private static async Task<StageProgressServerError?> ReadError(
        HttpResponseMessage response,
        CancellationToken cancellationToken) =>
        await response.Content.ReadFromJsonAsync<StageProgressServerError>(JsonOptions, cancellationToken);

    private sealed record StageResultSubmitRequest(string UserId, int StageId, bool WasVictory);
    private sealed record StageProgressServerError(string? ErrorCode, string? Message);
}

public sealed record StageProgressServerPayload(
    string UserId,
    int HighestClearedStageId,
    int NextUnlockedStageId,
    int[] ClearedStageIds,
    DateTimeOffset UpdatedAtUtc);

public sealed record StageRewardServerPayload(string RewardId, int Amount, string Reason);

public sealed record StageResultServerPayload(
    string UserId,
    int StageId,
    bool ProgressUpdated,
    int HighestClearedStageId,
    int NextUnlockedStageId,
    StageRewardServerPayload[] GrantedRewards,
    DateTimeOffset SavedAtUtc);

public sealed record StageProgressLoadResult(
    bool IsSuccess,
    string ErrorCode,
    string Message,
    StageProgressServerPayload? Payload)
{
    public static StageProgressLoadResult Succeeded(StageProgressServerPayload payload) =>
        new(true, "ok", "", payload);

    public static StageProgressLoadResult Failed(string errorCode, string message) =>
        new(false, errorCode, message, null);
}

public sealed record StageResultSubmitResult(
    bool IsSuccess,
    string ErrorCode,
    string Message,
    StageResultServerPayload? Payload)
{
    public static StageResultSubmitResult Succeeded(StageResultServerPayload payload) =>
        new(true, "ok", "", payload);

    public static StageResultSubmitResult Failed(string errorCode, string message) =>
        new(false, errorCode, message, null);
}
