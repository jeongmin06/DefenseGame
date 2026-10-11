using DefenseGame.Client.Data;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DefenseGame.Client.Network;

public sealed class SkillProfileServerClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<SkillProfileServerResult> LoadAsync(
        string baseUrl,
        string userId,
        CancellationToken cancellationToken)
    {
        try
        {
            string url = $"{NormalizeBaseUrl(baseUrl)}/v1/skill-profiles/{Uri.EscapeDataString(userId)}";
            using HttpResponseMessage response = await Http.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode) return await Failure(response, cancellationToken);
            SkillProfileServerPayload? payload = await response.Content
                .ReadFromJsonAsync<SkillProfileServerPayload>(JsonOptions, cancellationToken);
            return payload is null
                ? SkillProfileServerResult.Failed("invalid_response", "Server returned an empty skill profile response.")
                : SkillProfileServerResult.Succeeded(payload);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or UriFormatException)
        {
            return SkillProfileServerResult.Failed("server_unavailable", ex.Message);
        }
    }

    public async Task<SkillProfileServerResult> SaveAsync(
        string baseUrl,
        string userId,
        Godot.Collections.Array<CatSkillPreset> presets,
        int expectedRevision,
        bool isMigration,
        CancellationToken cancellationToken)
    {
        try
        {
            string url = $"{NormalizeBaseUrl(baseUrl)}/v1/skill-profiles";
            SkillPresetServerSaveRequest[] presetRequests = presets.Select(preset =>
                new SkillPresetServerSaveRequest(
                    preset.CharacterId,
                    preset.ActiveSkillId,
                    preset.SupportSkillIds.ToArray(),
                    preset.AllocatedPoints)).ToArray();
            var request = new SkillProfileServerSaveRequest(
                userId, presetRequests, expectedRevision, isMigration);
            using HttpResponseMessage response = await Http.PutAsJsonAsync(url, request, JsonOptions, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                SkillProfileServerPayload? payload = await response.Content
                    .ReadFromJsonAsync<SkillProfileServerPayload>(JsonOptions, cancellationToken);
                return payload is null
                    ? SkillProfileServerResult.Failed("invalid_response", "Server returned an empty skill profile response.")
                    : SkillProfileServerResult.Succeeded(payload);
            }
            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                SkillProfileServerError? conflict = await response.Content
                    .ReadFromJsonAsync<SkillProfileServerError>(JsonOptions, cancellationToken);
                return SkillProfileServerResult.Conflicted(
                    conflict?.Current,
                    conflict?.Message ?? "Skill profile revision conflict.");
            }
            return await Failure(response, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or UriFormatException)
        {
            return SkillProfileServerResult.Failed("server_unavailable", ex.Message);
        }
    }

    private static async Task<SkillProfileServerResult> Failure(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        SkillProfileServerError? error = await response.Content
            .ReadFromJsonAsync<SkillProfileServerError>(JsonOptions, cancellationToken);
        return SkillProfileServerResult.Failed(
            error?.ErrorCode ?? $"http_{(int)response.StatusCode}",
            error?.Message ?? response.ReasonPhrase ?? "Server request failed.");
    }

    private static string NormalizeBaseUrl(string baseUrl) => baseUrl.Trim().TrimEnd('/');

    private sealed record SkillPresetServerSaveRequest(
        string CharacterId,
        string ActiveSkillId,
        string[] SupportSkillIds,
        int AllocatedPoints);

    private sealed record SkillProfileServerSaveRequest(
        string UserId,
        SkillPresetServerSaveRequest[] Presets,
        int ExpectedRevision,
        bool IsMigration);

    private sealed record SkillProfileServerError(
        string? ErrorCode,
        string? Message,
        SkillProfileServerPayload? Current);
}

public sealed record SkillPresetServerPayload(
    string CharacterId,
    string ActiveSkillId,
    string[] SupportSkillIds,
    int AllocatedPoints,
    DateTimeOffset UpdatedAtUtc);

public sealed record SkillProfileServerPayload(
    string UserId,
    int PlayerLevel,
    int UnlockedPoints,
    string[] OwnedSkillIds,
    SkillPresetServerPayload[] Presets,
    int Revision,
    DateTimeOffset UpdatedAtUtc);

public sealed record SkillProfileServerResult(
    bool IsSuccess,
    bool IsConflict,
    string ErrorCode,
    string Message,
    SkillProfileServerPayload? Payload)
{
    public static SkillProfileServerResult Succeeded(SkillProfileServerPayload payload) =>
        new(true, false, "ok", "", payload);

    public static SkillProfileServerResult Failed(string errorCode, string message) =>
        new(false, false, errorCode, message, null);

    public static SkillProfileServerResult Conflicted(SkillProfileServerPayload? current, string message) =>
        new(false, true, "revision_conflict", message, current);
}
