using DefenseGame.Server.Contracts;

namespace DefenseGame.Server.Hosting;

public sealed class PlayerActorObservationFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        context.HttpContext.Items[RequestObservationMiddleware.ActorItemKey] = "PlayerActor";

        string? userId = context.HttpContext.Request.RouteValues["userId"]?.ToString();
        userId ??= context.Arguments.OfType<StageClearRequest>().FirstOrDefault()?.UserId;
        userId ??= context.Arguments.OfType<SaveStageSquadRequest>().FirstOrDefault()?.UserId;
        userId ??= context.Arguments.OfType<SaveSkillProfileRequest>().FirstOrDefault()?.UserId;

        if (!string.IsNullOrWhiteSpace(userId))
            context.HttpContext.Items[RequestObservationMiddleware.UserIdItemKey] = userId.Trim();

        return await next(context);
    }
}
