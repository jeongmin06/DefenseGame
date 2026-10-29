using DefenseGame.Server.Authentication;

namespace DefenseGame.Server.Hosting;

public sealed class GuestAuthenticationMiddleware(RequestDelegate next)
{
    public const string AuthenticatedPlayerIdItemKey = "DefenseGame.AuthenticatedPlayerId";
    private const string GuestLoginPath = "/v1/auth/guest";

    public async Task InvokeAsync(
        HttpContext context,
        GuestAuthenticationOptions options,
        GuestTokenService tokens)
    {
        if (!options.Required ||
            !context.Request.Path.StartsWithSegments("/v1") ||
            context.Request.Path.Equals(GuestLoginPath, StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        string authorization = context.Request.Headers.Authorization.ToString();
        const string bearerPrefix = "Bearer ";
        if (!authorization.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            await WriteUnauthorizedAsync(context, "missing_access_token", "Bearer access token is required.");
            return;
        }

        string token = authorization[bearerPrefix.Length..].Trim();
        GuestTokenValidationResult validation = tokens.Validate(token);
        if (validation.Status != GuestTokenValidationStatus.Valid)
        {
            string errorCode = validation.Status == GuestTokenValidationStatus.Expired
                ? "expired_access_token"
                : "invalid_access_token";
            await WriteUnauthorizedAsync(context, errorCode, "Bearer access token is invalid or expired.");
            return;
        }

        context.Items[AuthenticatedPlayerIdItemKey] = validation.PlayerId;
        context.Items[RequestObservationMiddleware.UserIdItemKey] = validation.PlayerId;
        await next(context);
    }

    private static async Task WriteUnauthorizedAsync(HttpContext context, string errorCode, string message)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { errorCode, message });
    }
}
