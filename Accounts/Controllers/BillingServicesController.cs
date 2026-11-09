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
    private const string ReProcessMenuRoute      = "/billing-services/re-process";
    private const string CptsProcessedMenuRoute  = "/billing-services/cpts-processed";
    private const string ArReportMenuRoute        = "/billing-services/ar-report";
    private const string CallListMenuRoute        = "/billing-services/call-list";
    private const string ClosedListMenuRoute      = "/billing-services/closed-list";
    private const string MasterListMenuRoute      = "/billing-services/master-list";
    private const string MicsReportMenuRoute      = "/billing-services/mics-report";
    private const string ClaimMasterListMenuRoute = "/billing-services/claim-master-list";
    private const string StatusRulesMenuRoute     = "/billing-services/status-rules";
    private const string CodeUsageMenuRoute       = "/billing-services/code-usage";

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

    // ── Child 1: Re Process ────────────────────────────────────────────────────
    [HttpGet("re-process")]
    public async Task<IActionResult> GetReProcess([FromQuery] DateOnly? dateTo, CancellationToken ct)
    {
        if (!await HasActionAsync(ReProcessMenuRoute, "VIEW", ct)) return Forbid();
        var tenantId = _tenant.TenantId ?? 0;
        var data = await _billingServices.ListReProcessAsync(tenantId, dateTo, ct);
        return Ok(data);
    }

    // ── Child 2: CPTs Processed ────────────────────────────────────────────────
    [HttpGet("cpts-processed")]
    public async Task<IActionResult> GetCptsProcessed([FromQuery] DateOnly? dateTo, CancellationToken ct)
    {
        if (!await HasActionAsync(CptsProcessedMenuRoute, "VIEW", ct)) return Forbid();
        var tenantId = _tenant.TenantId ?? 0;
        var data = await _billingServices.ListCptsProcessedAsync(tenantId, dateTo, ct);
        return Ok(data);
    }

    // ── Child 3: AR Report ─────────────────────────────────────────────────────
    [HttpGet("ar-report")]
    public async Task<IActionResult> GetArReport([FromQuery] DateOnly? dateTo, CancellationToken ct)
    {
        if (!await HasActionAsync(ArReportMenuRoute, "VIEW", ct)) return Forbid();
        var tenantId = _tenant.TenantId ?? 0;
        var data = await _billingServices.ListArReportAsync(tenantId, dateTo, ct);
        return Ok(data);
    }

    // ── Child 4: Call List ─────────────────────────────────────────────────────
    [HttpGet("call-list")]
    public async Task<IActionResult> GetCallList([FromQuery] DateOnly? dateTo, CancellationToken ct)
    {
        if (!await HasActionAsync(CallListMenuRoute, "VIEW", ct)) return Forbid();
        var tenantId = _tenant.TenantId ?? 0;
        var data = await _billingServices.ListCallListAsync(tenantId, dateTo, ct);
        return Ok(data);
    }

    // ── Child 5: Closed List ───────────────────────────────────────────────────
    [HttpGet("closed-list")]
    public async Task<IActionResult> GetClosedList(
        [FromQuery] DateOnly? dateFrom,
        [FromQuery] DateOnly? dateTo,
        CancellationToken ct)
    {
        if (!await HasActionAsync(ClosedListMenuRoute, "VIEW", ct)) return Forbid();
        var tenantId = _tenant.TenantId ?? 0;
        var data = await _billingServices.ListClosedListAsync(tenantId, dateFrom, dateTo, ct);
        return Ok(data);
    }

    // ── Child 6: Master List ───────────────────────────────────────────────────
    [HttpGet("master-list")]
    public async Task<IActionResult> GetMasterList(
        [FromQuery] DateOnly? dateFrom,
        [FromQuery] DateOnly? dateTo,
        CancellationToken ct)
    {
        if (!await HasActionAsync(MasterListMenuRoute, "VIEW", ct)) return Forbid();
        var tenantId = _tenant.TenantId ?? 0;
        var data = await _billingServices.ListMasterListAsync(tenantId, dateFrom, dateTo, ct);
        return Ok(data);
    }

    // ── Child 8: Mics Report ───────────────────────────────────────────────────
    [HttpGet("mics-report")]
    public async Task<IActionResult> GetMicsReport([FromQuery] DateOnly? dateTo, CancellationToken ct)
    {
        if (!await HasActionAsync(MicsReportMenuRoute, "VIEW", ct)) return Forbid();
        var tenantId = _tenant.TenantId ?? 0;
        var data = await _billingServices.ListMicsReportAsync(tenantId, dateTo, ct);
        return Ok(data);
    }

    // ── Child 9: Claim Master List ─────────────────────────────────────────────
    [HttpGet("claim-master-list")]
    public async Task<IActionResult> GetClaimMasterList(
        [FromQuery] DateOnly? dateFrom,
        [FromQuery] DateOnly? dateTo,
        CancellationToken ct)
    {
        if (!await HasActionAsync(ClaimMasterListMenuRoute, "VIEW", ct)) return Forbid();
        var tenantId = _tenant.TenantId ?? 0;
        var data = await _billingServices.ListClaimMasterListAsync(tenantId, dateFrom, dateTo, ct);
        return Ok(data);
    }

    // ── Child 10: Status Rules (CRUD) ──────────────────────────────────────────
    [HttpGet("status-rules")]
    public async Task<IActionResult> GetStatusRules(CancellationToken ct)
    {
        if (!await HasActionAsync(StatusRulesMenuRoute, "VIEW", ct)) return Forbid();
        var data = await _billingServices.ListStatusRulesAsync(ct);
        return Ok(data);
    }

    [HttpGet("status-rules/options")]
    public async Task<IActionResult> GetStatusOptions(CancellationToken ct)
    {
        if (!await HasActionAsync(StatusRulesMenuRoute, "VIEW", ct)) return Forbid();
        var data = await _billingServices.ListStatusOptionsAsync(ct);
        return Ok(data);
    }

    [HttpPost("status-rules")]
    public async Task<IActionResult> SaveStatusRule(
        [FromBody] BillingStatusRuleSaveRequestDto request,
        CancellationToken ct)
    {
        var action = request.Id > 0 ? "EDIT" : "ADD";
        if (!await HasActionAsync(StatusRulesMenuRoute, action, ct)) return Forbid();
        var userName = User.Identity?.Name;
        var result = await _billingServices.SaveStatusRuleAsync(request, userName, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpDelete("status-rules/{id:int}")]
    public async Task<IActionResult> DeleteStatusRule(int id, CancellationToken ct)
    {
        if (!await HasActionAsync(StatusRulesMenuRoute, "DELETE", ct)) return Forbid();
        var userName = User.Identity?.Name;
        var result = await _billingServices.DeleteStatusRuleAsync(id, userName, ct);
        return result.Success ? Ok(result) : NotFound(result);
    }

    // ── Child 11: Code Usage ───────────────────────────────────────────────────
    [HttpGet("code-usage")]
    public async Task<IActionResult> GetCodeUsage(
        [FromQuery] string? code,
        [FromQuery] DateOnly? dateFrom,
        [FromQuery] DateOnly? dateTo,
        CancellationToken ct)
    {
        if (!await HasActionAsync(CodeUsageMenuRoute, "VIEW", ct)) return Forbid();
        if (string.IsNullOrWhiteSpace(code))
            return Ok(Array.Empty<BillingCodeUsageRowDto>());
        var tenantId = _tenant.TenantId ?? 0;
        var data = await _billingServices.ListCodeUsageAsync(tenantId, code.Trim(), dateFrom, dateTo, ct);
        return Ok(data);
    }
}
