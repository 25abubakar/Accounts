using Accounts.Data;
using Accounts.Models;
using Accounts.Services.Interfaces;
using System.Security.Claims;

namespace Accounts.Middleware;

public sealed class SecurityAuditMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<SecurityAuditMiddleware> _logger;

    public SecurityAuditMiddleware(
        RequestDelegate next,
        ILogger<SecurityAuditMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(
        HttpContext context,
        ApplicationDbContext db,
        ITenantService tenantService)
    {
        if (HttpMethods.IsGet(context.Request.Method)
            || HttpMethods.IsHead(context.Request.Method)
            || HttpMethods.IsOptions(context.Request.Method))
        {
            await _next(context);
            return;
        }

        Exception? requestException = null;
        try
        {
            await _next(context);
        }
        catch (Exception exception)
        {
            requestException = exception;
            throw;
        }
        finally
        {
            try
            {
                db.SecurityAuditLogs.Add(new SecurityAuditLog
                {
                    TenantId = tenantService.IsSuperAdmin ? null : tenantService.TenantId,
                    UserId = context.User.FindFirstValue(ClaimTypes.NameIdentifier),
                    Method = context.Request.Method,
                    Path = context.Request.Path.Value ?? "/",
                    StatusCode = requestException == null
                        ? context.Response.StatusCode
                        : StatusCodes.Status500InternalServerError,
                    Succeeded = requestException == null && context.Response.StatusCode < 400,
                    RemoteIp = context.Connection.RemoteIpAddress?.ToString(),
                    TraceId = context.TraceIdentifier,
                    CreatedOnUtc = DateTime.UtcNow
                });
                await db.SaveChangesAsync(CancellationToken.None);
            }
            catch (Exception auditException)
            {
                _logger.LogError(
                    auditException,
                    "Failed to persist security audit event for trace {TraceId}.",
                    context.TraceIdentifier);
            }
        }
    }
}
