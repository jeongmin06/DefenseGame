using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DefenseGame.Server.Services;
using Microsoft.AspNetCore.WebUtilities;

namespace DefenseGame.Server.Authentication;

public sealed class GuestTokenService(
    GuestAuthenticationOptions options,
    ISystemClock clock)
{
    private const int CurrentVersion = 1;
    private const int MaxTokenLength = 4096;

    public GuestTokenIssueResult Issue(string playerId)
    {
        if (!options.CanIssueTokens)
            throw new InvalidOperationException("Guest token signing is not configured.");
        if (string.IsNullOrWhiteSpace(playerId) || playerId.Length > 128)
            throw new ArgumentException("playerId must contain 1 to 128 characters.", nameof(playerId));

        DateTimeOffset expiresAtUtc = clock.UtcNow.AddHours(options.TokenLifetimeHours);
        var payload = new GuestTokenPayload(CurrentVersion, playerId, expiresAtUtc.ToUnixTimeSeconds());
        string encodedPayload = WebEncoders.Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(payload));
        byte[] signature = Sign(encodedPayload);
        return new GuestTokenIssueResult(
            playerId,
            $"{encodedPayload}.{WebEncoders.Base64UrlEncode(signature)}",
            expiresAtUtc);
    }

    public GuestTokenValidationResult Validate(string? token)
    {
        if (!options.CanIssueTokens)
            return GuestTokenValidationResult.Failure(GuestTokenValidationStatus.NotConfigured);
        if (string.IsNullOrWhiteSpace(token) || token.Length > MaxTokenLength)
            return GuestTokenValidationResult.Failure(GuestTokenValidationStatus.Invalid);

        int separator = token.IndexOf('.');
        if (separator <= 0 || separator != token.LastIndexOf('.') || separator == token.Length - 1)
            return GuestTokenValidationResult.Failure(GuestTokenValidationStatus.Invalid);

        string encodedPayload = token[..separator];
        string encodedSignature = token[(separator + 1)..];

        byte[] suppliedSignature;
        try
        {
            suppliedSignature = WebEncoders.Base64UrlDecode(encodedSignature);
        }
        catch (FormatException)
        {
            return GuestTokenValidationResult.Failure(GuestTokenValidationStatus.Invalid);
        }

        byte[] expectedSignature = Sign(encodedPayload);
        if (suppliedSignature.Length != expectedSignature.Length ||
            !CryptographicOperations.FixedTimeEquals(suppliedSignature, expectedSignature))
        {
            return GuestTokenValidationResult.Failure(GuestTokenValidationStatus.Invalid);
        }

        GuestTokenPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<GuestTokenPayload>(WebEncoders.Base64UrlDecode(encodedPayload));
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            return GuestTokenValidationResult.Failure(GuestTokenValidationStatus.Invalid);
        }

        if (payload is null || payload.Version != CurrentVersion ||
            string.IsNullOrWhiteSpace(payload.PlayerId) || payload.PlayerId.Length > 128)
        {
            return GuestTokenValidationResult.Failure(GuestTokenValidationStatus.Invalid);
        }

        DateTimeOffset expiresAtUtc;
        try
        {
            expiresAtUtc = DateTimeOffset.FromUnixTimeSeconds(payload.ExpiresAtUnixSeconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            return GuestTokenValidationResult.Failure(GuestTokenValidationStatus.Invalid);
        }

        if (expiresAtUtc <= clock.UtcNow)
            return GuestTokenValidationResult.Failure(GuestTokenValidationStatus.Expired);

        return new GuestTokenValidationResult(
            GuestTokenValidationStatus.Valid,
            payload.PlayerId,
            expiresAtUtc);
    }

    private byte[] Sign(string encodedPayload)
    {
        byte[] key = Encoding.UTF8.GetBytes(options.SigningKey!);
        return HMACSHA256.HashData(key, Encoding.ASCII.GetBytes(encodedPayload));
    }

    private sealed record GuestTokenPayload(
        int Version,
        string PlayerId,
        long ExpiresAtUnixSeconds);
}

public sealed record GuestTokenIssueResult(
    string PlayerId,
    string AccessToken,
    DateTimeOffset ExpiresAtUtc);

public enum GuestTokenValidationStatus
{
    Valid,
    Invalid,
    Expired,
    NotConfigured
}

public sealed record GuestTokenValidationResult(
    GuestTokenValidationStatus Status,
    string? PlayerId,
    DateTimeOffset? ExpiresAtUtc)
{
    public static GuestTokenValidationResult Failure(GuestTokenValidationStatus status) =>
        new(status, null, null);
}
