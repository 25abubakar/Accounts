using System.Security.Claims;
using Accounts.Data;
using Accounts.DTOs;
using Accounts.Services.Interfaces;
using Accounts.Services.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Controllers;

[ApiController]
[Authorize]
[Route("api/billing-services")]
public sealed class BillingServicesController : ControllerBase
{
    private const string ReProcessMenuRoute = "/billing-services/re-process";
    private const string CptsProcessedMenuRoute = "/billing-services/cpts-processed";
    private const string ArReportMenuRoute = "/billing-services/ar-report";
    private const string StatusRulesMenuRoute = "/billing-services/status-rules";
    private readonly ApplicationDbContext _db;
    private readonly ITenantService _tenant;
    private readonly TenantPermissionService _tenantPermissions;
    private readonly RbacService _rbac;
    private readonly IBillingServicesService _billingServices;

    public BillingServicesController(
        ApplicationDbContext db,
        ITenantService tenant,
        TenantPermissionService tenantPermissions,
        RbacService rbac,
        IBillingServicesService billingServices)
    {
        _db = db;
        _tenant = tenant;
        _tenantPermissions = tenantPermissions;
        _rbac = rbac;
        _billingServices = billingServices;
    }

    private string? UserId() => User.FindFirstValue(ClaimTypes.NameIdentifier);

    private async Task<bool> HasActionAsync(string route, string action, CancellationToken ct)
    {
        if (TenantPermissionService.IsSuperAdmin(User)) return true;
        if (TenantPermissionService.IsTenantAdmin(User))
            return await _tenantPermissions.HasMenuRouteAsync(User, [route], action, ct);
        if (!_tenant.TenantId.HasValue) return false;
        var userId = UserId();
        if (string.IsNullOrWhiteSpace(userId)) return false;
        var staffId = await _db.Persons.AsNoTracking()
            .Where(x => x.IdentityUserId == userId && x.Staff != null)
            .Select(x => (Guid?)x.Staff!.StaffId)
            .FirstOrDefaultAsync(ct);
        if (!staffId.HasValue) return false;
        var menuId = await _db.Menus.AsNoTracking()
            .Where(x => x.IsActive && x.Route == route)
            .Select(x => (int?)x.Id)
            .FirstOrDefaultAsync(ct);
        if (!menuId.HasValue) return false;
        var normalized = action.Trim().ToUpperInvariant();
        if (normalized == "VIEW" && await _rbac.HasAccessAsync(staffId.Value, $"MENU_{menuId.Value}"))
            return true;
        return await _rbac.HasAccessAsync(staffId.Value, $"MENU_{menuId.Value}_{normalized}");
    }

    /// <summary>
    /// Gets Re Process claims list.
    /// </summary>
    [HttpGet("re-process")]
    public async Task<IActionResult> GetReProcess([FromQuery] DateOnly? dateTo, CancellationToken ct)
    {
        if (!await HasActionAsync(ReProcessMenuRoute, "VIEW", ct)) return Forbid();
        var tenantId = _tenant.TenantId ?? 0;
        var data = await _billingServices.ListReProcessAsync(tenantId, dateTo, ct);
        return Ok(data);
    }

    /// <summary>
    /// Gets CPTs Processed claims list.
    /// </summary>
    [HttpGet("cpts-processed")]
    public async Task<IActionResult> GetCptsProcessed([FromQuery] DateOnly? dateTo, CancellationToken ct)
    {
        if (!await HasActionAsync(CptsProcessedMenuRoute, "VIEW", ct)) return Forbid();
        var tenantId = _tenant.TenantId ?? 0;
        var data = await _billingServices.ListCptsProcessedAsync(tenantId, dateTo, ct);
        return Ok(data);
    }

    /// <summary>
    /// Gets AR Report claims list.
    /// </summary>
    [HttpGet("ar-report")]
    public async Task<IActionResult> GetArReport([FromQuery] DateOnly? dateTo, CancellationToken ct)
    {
        if (!await HasActionAsync(ArReportMenuRoute, "VIEW", ct)) return Forbid();
        var tenantId = _tenant.TenantId ?? 0;
        var data = await _billingServices.ListArReportAsync(tenantId, dateTo, ct);
        return Ok(data);
    }
}
