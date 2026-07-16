namespace Accounts.Middleware;

/// <summary>
/// Treats a request-aborted cancellation as a normal client disconnect.
/// Database operations must continue to receive RequestAborted so abandoned
/// queries stop promptly instead of consuming a connection in the background.
/// </summary>
public sealed class RequestCancellationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestCancellationMiddleware> _logger;

    public RequestCancellationMiddleware(
        RequestDelegate next,
        ILogger<RequestCancellationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            _logger.LogDebug(
                "Request {Method} {Path} was cancelled by the client.",
                context.Request.Method,
                context.Request.Path);

            if (!context.Response.HasStarted)
            {
                context.Response.Clear();
                // 499 is the conventional status for a client-closed request.
                context.Response.StatusCode = 499;
            }
        }
    }
}
