using Accounts.Data;
using Accounts.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Middleware;

/// <summary>
/// Defense-in-depth boundary applied before MVC authorization:
/// platform actors cannot call tenant operational APIs, and TenantAdmin CRUD
/// requests must remain inside the SuperAdmin-approved menu ceiling.
/// </summary>
public sealed class OperationalAccessBoundaryMiddleware
{
    private readonly RequestDelegate _next;

    private static readonly IReadOnlyDictionary<string, string[]> TenantRouteMap =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["/api/persons"] = ["/hr/staff", "/hr/persons"],
            ["/api/employees"] = ["/hr/staff"],
            ["/api/report-to"] = ["/hr/staff"],
            ["/api/positions"] = ["/hr/vacancies", "/hr/positions", "/positions"],
            ["/api/job-titles"] = ["/settings/job-titles"],
            ["/api/salary-scales"] = ["/settings/scales"],
            ["/api/process-workflow"] = ["/hr/process/report", "/hr/process/task-list"],
            ["/api/process-category-approvers"] = ["/hr/process/report"],
            ["/api/app-notes"] = ["/instructions", "/settings/instruction"],
            ["/api/access"] = ["/access/admin", "/access/groups"],
            ["/api/rbac"] = ["/access/admin", "/access/groups"],
            ["/api/staff-menu-access"] = ["/access/admin", "/access/groups"],
            ["/api/tenant-roles"] = ["/roles", "/security/roles"],
            ["/api/dashboard"] = ["/dashboard"],
            ["/api/organization"] = ["/organization", "/groups/companies", "/groups/hierarchy"],
            ["/api/attendance"] = ["/attendance"],
            ["/api/attendance-status"] = ["/settings/statuses", "/attendance/rules/list"],
            ["/api/status-configurations"] = ["/settings/statuses"]
        };

    public OperationalAccessBoundaryMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(
        HttpContext context,
        ITenantService tenantService,
        ApplicationDbContext db)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            await _next(context);
            return;
        }

        var path = context.Request.Path.Value ?? string.Empty;
        if (tenantService.IsSuperAdmin)
        {
            if (!IsPlatformPathAllowed(path))
            {
                await WriteForbiddenAsync(
                    context,
                    "PLATFORM_OPERATIONAL_ACCESS_DENIED",
                    "SuperAdmin cannot access tenant operational data.");
                return;
            }

            await _next(context);
            return;
        }

        if (tenantService.IsTenantAdmin)
        {
            if (!tenantService.TenantId.HasValue)
            {
                await WriteForbiddenAsync(
                    context,
                    "TENANT_CONTEXT_REQUIRED",
                    "A verified tenant context is required.");
                return;
            }

            var routes = ResolveTenantRoutes(path);
            if (routes != null)
            {
                var method = context.Request.Method;
                var isDelegationOperation =
                    path.StartsWith("/api/access", StringComparison.OrdinalIgnoreCase)
                    || path.StartsWith("/api/staff-menu-access", StringComparison.OrdinalIgnoreCase)
                    || path.StartsWith("/api/tenant-roles", StringComparison.OrdinalIgnoreCase);
                var includesAttendancePrefix = routes.Contains(
                    "/attendance",
                    StringComparer.OrdinalIgnoreCase);
                var exactRoutes = routes
                    .Where(route => !route.Equals("/attendance", StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                var allowed = await db.TenantMenuPermissions
                    .AsNoTracking()
                    .Where(grant =>
                        grant.TenantId == tenantService.TenantId.Value
                        && grant.IsAllow
                        && grant.CanView
                        && grant.Menu != null
                        && grant.Menu.IsActive
                        && grant.Menu.Route != null
                        && (exactRoutes.Contains(grant.Menu.Route)
                            || (includesAttendancePrefix
                                && grant.Menu.Route.StartsWith("/attendance"))))
                    .AnyAsync(grant =>
                            HttpMethods.IsGet(method) || HttpMethods.IsHead(method)
                                ? grant.CanView
                                : isDelegationOperation
                                    ? grant.CanEdit
                                : HttpMethods.IsPost(method)
                                    ? grant.CanAdd
                                    : HttpMethods.IsPut(method) || HttpMethods.IsPatch(method)
                                        ? grant.CanEdit
                                        : HttpMethods.IsDelete(method) && grant.CanDelete,
                        context.RequestAborted);

                if (!allowed)
                {
                    await WriteForbiddenAsync(
                        context,
                        "TENANT_CEILING_DENIED",
                        "The SuperAdmin-approved tenant ceiling does not permit this operation.");
                    return;
                }
            }
        }

        await _next(context);
    }

    private static string[]? ResolveTenantRoutes(string path) =>
        TenantRouteMap
            .Where(entry => path.StartsWith(entry.Key, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(entry => entry.Key.Length)
            .Select(entry => entry.Value)
            .FirstOrDefault();

    private static bool IsPlatformPathAllowed(string path)
    {
        var allowedPrefixes = new[]
        {
            "/api/auth",
            "/api/security",
            "/api/tenants",
            "/api/tenant-management",
            "/api/tenant-branding",
            "/api/organization",
            "/api/menus",
            "/api/app-menu-definitions",
            "/api/app-lookups",
            "/api/locations",
            "/api/v2/menu",
            "/api/attendance-status",
            "/api/status-configurations",
            "/api/app-notes",
            "/health"
        };
        if (allowedPrefixes.Any(prefix =>
                path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            return true;

        return path.Equals("/api/rbac/features", StringComparison.OrdinalIgnoreCase)
               || path.Equals("/api/rbac/seed-features", StringComparison.OrdinalIgnoreCase);
    }

    private static Task WriteForbiddenAsync(
        HttpContext context,
        string code,
        string message)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return context.Response.WriteAsJsonAsync(new { code, message }, context.RequestAborted);
    }
}
