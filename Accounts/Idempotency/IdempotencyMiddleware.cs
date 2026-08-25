using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Accounts.Services.Interfaces;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Accounts.Idempotency;

public sealed class IdempotencyMiddleware(
    IIdempotencyStore store,
    ITenantService tenant,
    TimeProvider timeProvider,
    IOptions<IdempotencyOptions> options,
    ILogger<IdempotencyMiddleware> logger) : IMiddleware
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IdempotencyOptions _options = options.Value;

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        try
        {
            await InvokeCoreAsync(context, next);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            logger.LogDebug(
                "Request {TraceIdentifier} was canceled by the client; idempotency processing stopped cleanly.",
                context.TraceIdentifier);
        }
    }

    private async Task InvokeCoreAsync(HttpContext context, RequestDelegate next)
    {
        var metadata = context.GetEndpoint()?.Metadata.GetMetadata<IdempotentAttribute>();
        if (metadata is null)
        {
            await next(context);
            return;
        }

        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            await WriteProblemAsync(
                context,
                StatusCodes.Status401Unauthorized,
                "Authentication required",
                "An authenticated user is required for an idempotent command.");
            return;
        }

        if (!TryReadKey(context, out var key, out var keyError))
        {
            await WriteProblemAsync(
                context,
                StatusCodes.Status400BadRequest,
                "Invalid idempotency key",
                keyError);
            return;
        }

        byte[] requestHash;
        try
        {
            requestHash = await ComputeRequestHashAsync(context.Request, context.RequestAborted);
        }
        catch (RequestBodyTooLargeException)
        {
            await WriteProblemAsync(
                context,
                StatusCodes.Status413PayloadTooLarge,
                "Request is too large",
                $"Idempotent requests are limited to {_options.MaxRequestBodyBytes} bytes.");
            return;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var ttlHours = metadata.TtlHours > 0 ? metadata.TtlHours : _options.DefaultTtlHours;
        var path = (context.Request.PathBase + context.Request.Path).Value ?? "/";
        var command = new IdempotencyCommand(
            key,
            ComputeScopeHash(tenant.TenantId, userId),
            requestHash,
            tenant.TenantId,
            userId,
            context.Request.Method.ToUpperInvariant(),
            path,
            now,
            now.AddSeconds(_options.ProcessingLeaseSeconds),
            now.AddHours(ttlHours));

        IdempotencyBeginResult begin;
        try
        {
            begin = await store.BeginAsync(command, context.RequestAborted);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not acquire idempotency key {Key}.", key);
            await WriteProblemAsync(
                context,
                StatusCodes.Status503ServiceUnavailable,
                "Idempotency service unavailable",
                "The command was not executed because duplicate-request protection is unavailable.");
            return;
        }

        switch (begin.State)
        {
            case IdempotencyBeginState.Completed:
                await ReplayAsync(context, key, begin.Response!);
                return;
            case IdempotencyBeginState.PayloadMismatch:
                await WriteProblemAsync(
                    context,
                    StatusCodes.Status409Conflict,
                    "Idempotency key payload mismatch",
                    "This idempotency key was already used with a different endpoint, query, or request body.");
                return;
            case IdempotencyBeginState.Processing:
                SetRetryAfter(context, begin.LeaseExpiresUtc);
                await WriteProblemAsync(
                    context,
                    StatusCodes.Status409Conflict,
                    "Request already processing",
                    begin.LeaseExpiresUtc <= now
                        ? "The previous execution is in an indeterminate state and requires reconciliation."
                        : "A request with this idempotency key is already processing.");
                return;
            case IdempotencyBeginState.Failed:
                await WriteProblemAsync(
                    context,
                    StatusCodes.Status409Conflict,
                    "Previous execution requires reconciliation",
                    "The previous execution failed or ended in an indeterminate state. Do not repeat a financial operation until its outcome is verified.");
                return;
            case IdempotencyBeginState.Acquired:
                break;
            default:
                throw new InvalidOperationException("Unknown idempotency state.");
        }

        var recordId = begin.RecordId!.Value;
        var lockToken = begin.LockToken!.Value;
        var originalBody = context.Response.Body;
        await using var responseBuffer = new MemoryStream();
        context.Response.Body = responseBuffer;
        context.Response.Headers["X-Idempotency-Key"] = key.ToString("D");
        var recordFinalized = false;

        try
        {
            await next(context);

            if (responseBuffer.Length > _options.MaxResponseBodyBytes)
            {
                await store.FailAsync(
                    recordId,
                    lockToken,
                    "The response exceeded the configured idempotency storage limit.",
                    release: false,
                    timeProvider.GetUtcNow().UtcDateTime,
                    CancellationToken.None);
                recordFinalized = true;
                context.Response.Body = originalBody;
                context.Response.Clear();
                await WriteProblemAsync(
                    context,
                    StatusCodes.Status500InternalServerError,
                    "Response cannot be safely cached",
                    "The operation outcome requires reconciliation before retrying.");
                return;
            }

            if (context.Response.StatusCode >= StatusCodes.Status500InternalServerError)
            {
                await store.FailAsync(
                    recordId,
                    lockToken,
                    $"Endpoint returned HTTP {context.Response.StatusCode}.",
                    metadata.ReleaseOnFailure,
                    timeProvider.GetUtcNow().UtcDateTime,
                    CancellationToken.None);
                recordFinalized = true;
            }
            else
            {
                var storedResponse = new StoredIdempotencyResponse(
                    context.Response.StatusCode,
                    context.Response.ContentType,
                    SerializeReplayHeaders(context.Response),
                    responseBuffer.ToArray());
                await store.CompleteAsync(
                    recordId,
                    lockToken,
                    storedResponse,
                    timeProvider.GetUtcNow().UtcDateTime,
                    CancellationToken.None);
                recordFinalized = true;
            }

            context.Response.Body = originalBody;
            responseBuffer.Position = 0;
            await responseBuffer.CopyToAsync(originalBody, context.RequestAborted);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            context.Response.Body = originalBody;
            if (!recordFinalized)
            {
                try
                {
                    await store.FailAsync(
                        recordId,
                        lockToken,
                        "RequestCanceledAfterExecutionStarted",
                        release: false,
                        timeProvider.GetUtcNow().UtcDateTime,
                        CancellationToken.None);
                }
                catch (Exception storageException)
                {
                    logger.LogError(
                        storageException,
                        "Could not mark canceled idempotency record {RecordId} for reconciliation.",
                        recordId);
                }
            }

            throw;
        }
        catch (Exception exception)
        {
            context.Response.Body = originalBody;
            if (!recordFinalized)
            {
                try
                {
                    await store.FailAsync(
                        recordId,
                        lockToken,
                        exception.GetType().Name,
                        metadata.ReleaseOnFailure,
                        timeProvider.GetUtcNow().UtcDateTime,
                        CancellationToken.None);
                }
                catch (Exception storageException)
                {
                    logger.LogError(
                        storageException,
                        "Could not record failure for idempotency record {RecordId}.",
                        recordId);
                }
            }

            throw;
        }
    }

    private bool TryReadKey(HttpContext context, out Guid key, out string error)
    {
        key = Guid.Empty;
        error = $"Send one UUID/GUID in the {_options.HeaderName} header.";
        if (!context.Request.Headers.TryGetValue(_options.HeaderName, out var values) || values.Count != 1)
            return false;

        if (!Guid.TryParse(values[0], out key) || key == Guid.Empty)
            return false;

        error = string.Empty;
        return true;
    }

    private async Task<byte[]> ComputeRequestHashAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ContentLength > _options.MaxRequestBodyBytes)
            throw new RequestBodyTooLargeException();

        request.EnableBuffering();
        request.Body.Position = 0;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var descriptor = string.Join(
            '\n',
            request.Method.ToUpperInvariant(),
            (request.PathBase + request.Path).Value ?? "/",
            request.QueryString.Value ?? string.Empty,
            request.ContentType ?? string.Empty);
        hash.AppendData(Encoding.UTF8.GetBytes(descriptor));

        var buffer = new byte[81920];
        var total = 0;
        int read;
        while ((read = await request.Body.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total += read;
            if (total > _options.MaxRequestBodyBytes)
            {
                request.Body.Position = 0;
                throw new RequestBodyTooLargeException();
            }

            hash.AppendData(buffer, 0, read);
        }

        request.Body.Position = 0;
        return hash.GetHashAndReset();
    }

    private static byte[] ComputeScopeHash(int? tenantId, string userId) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{tenantId?.ToString() ?? "global"}\n{userId.Trim().ToUpperInvariant()}"));

    private string? SerializeReplayHeaders(HttpResponse response)
    {
        var headers = _options.ReplayHeaders
            .Where(name => response.Headers.ContainsKey(name))
            .ToDictionary(name => name, name => response.Headers[name].ToArray());
        return headers.Count == 0 ? null : JsonSerializer.Serialize(headers, JsonOptions);
    }

    private static async Task ReplayAsync(
        HttpContext context,
        Guid key,
        StoredIdempotencyResponse response)
    {
        context.Response.StatusCode = response.StatusCode;
        context.Response.ContentType = response.ContentType;
        context.Response.Headers["Idempotency-Replayed"] = "true";
        context.Response.Headers["X-Idempotency-Key"] = key.ToString("D");

        if (!string.IsNullOrWhiteSpace(response.HeadersJson))
        {
            var headers = JsonSerializer.Deserialize<Dictionary<string, string[]>>(
                response.HeadersJson,
                JsonOptions);
            if (headers is not null)
            {
                foreach (var header in headers)
                    context.Response.Headers[header.Key] = header.Value;
            }
        }

        context.Response.ContentLength = response.Body.Length;
        await context.Response.Body.WriteAsync(response.Body, context.RequestAborted);
    }

    private static void SetRetryAfter(HttpContext context, DateTime? leaseExpiresUtc)
    {
        if (!leaseExpiresUtc.HasValue)
            return;

        var seconds = (int)Math.Ceiling((leaseExpiresUtc.Value - DateTime.UtcNow).TotalSeconds);
        if (seconds > 0)
            context.Response.Headers[HeaderNames.RetryAfter] = seconds.ToString();
    }

    private static async Task WriteProblemAsync(
        HttpContext context,
        int statusCode,
        string title,
        string detail)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";
        var body = JsonSerializer.SerializeToUtf8Bytes(new
        {
            type = "about:blank",
            title,
            status = statusCode,
            detail,
            traceId = context.TraceIdentifier
        }, JsonOptions);
        context.Response.ContentLength = body.Length;
        await context.Response.Body.WriteAsync(body, context.RequestAborted);
    }

    private sealed class RequestBodyTooLargeException : Exception
    {
    }
}
