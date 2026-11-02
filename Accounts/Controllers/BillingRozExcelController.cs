using System.Security.Claims;
using Accounts.Data;
using Accounts.DTOs;
using Accounts.Idempotency;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Accounts.Services.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Controllers;

[ApiController]
[Authorize]
[Route("api/billing/roz-excel")]
public sealed class BillingRozExcelController : ControllerBase
{
    private const string MenuRoute = "/billing/roz-excel";
    private readonly ApplicationDbContext _db;
    private readonly ITenantService _tenant;
    private readonly TenantPermissionService _tenantPermissions;
    private readonly RbacService _rbac;
    private readonly IBillingRozExcelService _rozExcel;

    public BillingRozExcelController(
        ApplicationDbContext db,
        ITenantService tenant,
        TenantPermissionService tenantPermissions,
        RbacService rbac,
        IBillingRozExcelService rozExcel)
    {
        _db = db;
        _tenant = tenant;
        _tenantPermissions = tenantPermissions;
        _rbac = rbac;
        _rozExcel = rozExcel;
    }

    private string? UserId() => User.FindFirstValue(ClaimTypes.NameIdentifier);

    private async Task<bool> HasActionAsync(string action, CancellationToken ct)
    {
        if (TenantPermissionService.IsSuperAdmin(User)) return true;
        if (TenantPermissionService.IsTenantAdmin(User))
            return await _tenantPermissions.HasMenuRouteAsync(User, [MenuRoute], action, ct);
        if (!_tenant.TenantId.HasValue) return false;
        var userId = UserId();
        if (string.IsNullOrWhiteSpace(userId)) return false;
        var staffId = await _db.Persons.AsNoTracking()
            .Where(x => x.IdentityUserId == userId && x.Staff != null)
            .Select(x => (Guid?)x.Staff!.StaffId)
            .FirstOrDefaultAsync(ct);
        if (!staffId.HasValue) return false;
        var menuId = await _db.Menus.AsNoTracking()
            .Where(x => x.IsActive && x.Route == MenuRoute)
            .Select(x => (int?)x.Id)
            .FirstOrDefaultAsync(ct);
        if (!menuId.HasValue) return false;
        var normalized = action.Trim().ToUpperInvariant();
        if (normalized == "VIEW" && await _rbac.HasAccessAsync(staffId.Value, $"MENU_{menuId.Value}"))
            return true;
        return await _rbac.HasAccessAsync(staffId.Value, $"MENU_{menuId.Value}_{normalized}");
    }

    [HttpGet("staged")]
    public async Task<IActionResult> ListStaged(
        [FromQuery] DateOnly? dateFrom,
        [FromQuery] DateOnly? dateTo,
        [FromQuery] bool? isSettled,
        CancellationToken ct)
    {
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue || !await HasActionAsync("VIEW", ct))
            return Forbid();

        var rows = await _rozExcel.ListStagedAsync(_tenant.RequiredTenantId, dateFrom, dateTo, isSettled, ct);
        return Ok(rows);
    }

    [HttpPost("upload")]
    [RequestSizeLimit(20 * 1024 * 1024)]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Upload(IFormFile file, CancellationToken ct)
    {
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue || !await HasActionAsync("ADD", ct))
            return Forbid();

        var count = await _rozExcel.UploadBillingExcelAsync(_tenant.RequiredTenantId, file, UserId(), ct);
        return Ok(new { success = true, count, message = $"{count} row(s) uploaded into billing staging successfully." });
    }

    [HttpPost("settle")]
    [Idempotent]
    public async Task<IActionResult> Settle([FromBody] BillingSettleRequest request, CancellationToken ct)
    {
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue || !await HasActionAsync("EDIT", ct))
            return Forbid();

        var intIds = request.Ids.Select(x => (int)x).ToList();
        var count = await _rozExcel.SettleStagedIntoRoznamchaAsync(_tenant.RequiredTenantId, intIds, UserId(), ct);
        return Ok(new { success = true, count, message = $"{count} staged row(s) settled into live Roznamcha successfully." });
    }

    [HttpDelete("staged/{id:int}")]
    public async Task<IActionResult> DeleteStaged(int id, CancellationToken ct)
    {
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue || !await HasActionAsync("DELETE", ct))
            return Forbid();

        var ok = await _rozExcel.DeleteStagedAsync(_tenant.RequiredTenantId, id, ct);
        return ok ? Ok(new { success = true, message = "Staged row deleted." }) : NotFound(new { message = "Staged row was not found." });
    }

    [HttpGet("summary")]
    public async Task<IActionResult> Summary(CancellationToken ct)
    {
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue || !await HasActionAsync("VIEW", ct))
            return Forbid();

        var summary = await _rozExcel.GetSummaryAsync(_tenant.RequiredTenantId, ct);
        return Ok(summary);
    }
}
