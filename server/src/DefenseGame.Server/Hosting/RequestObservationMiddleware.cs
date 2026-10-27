using System.Diagnostics;

namespace DefenseGame.Server.Hosting;

public sealed class RequestObservationMiddleware(
    RequestDelegate next,
    ILogger<RequestObservationMiddleware> logger)
{
    public const string ActorItemKey = "DefenseGame.Actor";
    public const string UserIdItemKey = "DefenseGame.UserId";
    private const string RequestIdHeader = "X-Request-ID";
    private const int MaxRequestIdLength = 128;

    public async Task InvokeAsync(HttpContext context)
    {
        string requestId = ResolveRequestId(context);
        context.Response.Headers[RequestIdHeader] = requestId;
        long startedAt = Stopwatch.GetTimestamp();
        Exception? failure = null;

        try
        {
            await next(context);
        }
        catch (Exception exception)
        {
            failure = exception;
            throw;
        }
        finally
        {
            int statusCode = failure is null ? context.Response.StatusCode : StatusCodes.Status500InternalServerError;
            string result = statusCode < StatusCodes.Status400BadRequest ? "success" : "failure";
            double durationMs = Math.Round(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds, 3);
            string? userId = context.Items[UserIdItemKey] as string;
            string actor = context.Items[ActorItemKey] as string ?? "None";

            logger.Log(
                failure is null ? LogLevel.Information : LogLevel.Error,
                failure,
                "HTTP request completed {requestId} {userId} {actor} {durationMs} {result} {method} {path} {statusCode}",
                requestId,
                userId,
                actor,
                durationMs,
                result,
                context.Request.Method,
                context.Request.Path.Value,
                statusCode);
        }
    }

    private static string ResolveRequestId(HttpContext context)
    {
        string? requestedId = context.Request.Headers[RequestIdHeader].FirstOrDefault()?.Trim();
        return !string.IsNullOrEmpty(requestedId) && requestedId.Length <= MaxRequestIdLength
            ? requestedId
            : Guid.NewGuid().ToString("N");
    }
}
