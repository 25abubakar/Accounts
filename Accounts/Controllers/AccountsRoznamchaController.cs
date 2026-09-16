using System.Security.Claims;
using Accounts.Data;
using Accounts.Services.Interfaces;
using Accounts.Services.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Controllers;

[ApiController]
[Route("api/accounts/roznamcha")]
[Authorize]
[Produces("application/json")]
public sealed class AccountsRoznamchaController : ControllerBase
{
    private const string PaymentRozRoute = "/accounts/payment-roz";

    private readonly ApplicationDbContext _db;
    private readonly ITenantService _tenant;
    private readonly TenantPermissionService _tenantPermissions;
    private readonly RbacService _rbac;
    private readonly IAccountsRoznamchaService _roznamcha;

    public AccountsRoznamchaController(
        ApplicationDbContext db,
        ITenantService tenant,
        TenantPermissionService tenantPermissions,
        RbacService rbac,
        IAccountsRoznamchaService roznamcha)
    {
        _db = db;
        _tenant = tenant;
        _tenantPermissions = tenantPermissions;
        _rbac = rbac;
        _roznamcha = roznamcha;
    }

    /// <summary>Payment (ROZ) list for the selected date range. Empty DB → empty array.</summary>
    [HttpGet("payment-roz")]
    public async Task<IActionResult> ListPaymentRoz(
        [FromQuery] DateOnly? dateFrom,
        [FromQuery] DateOnly? dateTo,
        CancellationToken ct)
    {
        if (_tenant.IsSuperAdmin)
            return Ok(Array.Empty<object>());

        if (!_tenant.TenantId.HasValue || !await HasActionAsync(PaymentRozRoute, "VIEW", ct))
            return Forbid();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = dateFrom ?? new DateOnly(today.Year, today.Month, 1);
        var to = dateTo ?? today;
        if (to < from)
            return BadRequest(new { message = "dateTo must be on or after dateFrom." });

        var rows = await _roznamcha.ListPaymentRozAsync(_tenant.RequiredTenantId, from, to, ct);
        return Ok(rows);
    }

    private async Task<bool> HasActionAsync(string route, string action, CancellationToken ct)
    {
        if (TenantPermissionService.IsSuperAdmin(User)) return true;
        if (TenantPermissionService.IsTenantAdmin(User))
            return await _tenantPermissions.HasMenuRouteAsync(User, [route], action, ct);
        if (!_tenant.TenantId.HasValue) return false;
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
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
}
