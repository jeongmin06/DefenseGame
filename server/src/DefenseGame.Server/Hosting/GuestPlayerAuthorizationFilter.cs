using DefenseGame.Server.Authentication;
using DefenseGame.Server.Contracts;

namespace DefenseGame.Server.Hosting;

public sealed class GuestPlayerAuthorizationFilter(
    GuestAuthenticationOptions options) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        if (!options.Required)
            return await next(context);

        string? authenticatedPlayerId =
            context.HttpContext.Items[GuestAuthenticationMiddleware.AuthenticatedPlayerIdItemKey] as string;
        if (string.IsNullOrEmpty(authenticatedPlayerId))
        {
            return Results.Json(
                new { errorCode = "missing_access_token", message = "Bearer access token is required." },
                statusCode: StatusCodes.Status401Unauthorized);
        }

        string? requestedPlayerId = ResolveRequestedPlayerId(context);
        if (string.IsNullOrWhiteSpace(requestedPlayerId) ||
            !string.Equals(requestedPlayerId.Trim(), authenticatedPlayerId, StringComparison.Ordinal))
        {
            return Results.Json(
                new
                {
                    errorCode = "authenticated_user_mismatch",
                    message = "The requested userId does not match the authenticated player."
                },
                statusCode: StatusCodes.Status403Forbidden);
        }

        return await next(context);
    }

    private static string? ResolveRequestedPlayerId(EndpointFilterInvocationContext context)
    {
        string? routePlayerId = context.HttpContext.Request.RouteValues["userId"]?.ToString();
        if (routePlayerId is not null)
            return routePlayerId;

        return context.Arguments.OfType<StageClearRequest>().FirstOrDefault()?.UserId
            ?? context.Arguments.OfType<SaveStageSquadRequest>().FirstOrDefault()?.UserId
            ?? context.Arguments.OfType<SaveSkillProfileRequest>().FirstOrDefault()?.UserId;
    }
}
