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
[Route("api/billing/patients")]
public sealed class BillingPatientsController : ControllerBase
{
    private const string MenuRoute = "/billing/patients";
    private readonly ApplicationDbContext _db;
    private readonly ITenantService _tenant;
    private readonly TenantPermissionService _tenantPermissions;
    private readonly RbacService _rbac;
    private readonly IBillingPatientsService _patients;

    public BillingPatientsController(
        ApplicationDbContext db,
        ITenantService tenant,
        TenantPermissionService tenantPermissions,
        RbacService rbac,
        IBillingPatientsService patients)
    {
        _db = db;
        _tenant = tenant;
        _tenantPermissions = tenantPermissions;
        _rbac = rbac;
        _patients = patients;
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

    [HttpGet("appointments")]
    public async Task<IActionResult> ListAppointments(
        [FromQuery] DateOnly? dateFrom,
        [FromQuery] DateOnly? dateTo,
        [FromQuery] int? officeId,
        CancellationToken ct)
    {
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue || !await HasActionAsync("VIEW", ct))
            return Forbid();

        var rows = await _patients.ListAppointmentsAsync(_tenant.RequiredTenantId, dateFrom, dateTo, officeId, ct);
        return Ok(rows);
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string q, CancellationToken ct)
    {
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue || !await HasActionAsync("VIEW", ct))
            return Forbid();

        var rows = await _patients.SearchPatientsAsync(_tenant.RequiredTenantId, q, ct);
        return Ok(rows);
    }

    [HttpPost("appointments")]
    [Idempotent]
    public async Task<IActionResult> CreateAppointment([FromBody] CreatePatientAppointmentRequest request, CancellationToken ct)
    {
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue || !await HasActionAsync("ADD", ct))
            return Forbid();

        try
        {
            var apptId = await _patients.CreatePatientAndAppointmentAsync(_tenant.RequiredTenantId, request, UserId(), ct);
            return Ok(new { success = true, appointmentId = apptId, message = "Patient and appointment created successfully." });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    [HttpPost("upload-sheet")]
    [RequestSizeLimit(20 * 1024 * 1024)]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UploadPatientSheet(IFormFile file, CancellationToken ct)
    {
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue || !await HasActionAsync("ADD", ct))
            return Forbid();

        var count = await _patients.UploadPatientSheetAsync(_tenant.RequiredTenantId, file, UserId(), ct);
        return Ok(new { success = true, count, message = $"{count} patient appointment(s) imported successfully." });
    }

    [HttpPost("sync-emr")]
    [Idempotent]
    public async Task<IActionResult> SyncEmr([FromQuery] DateOnly? date, CancellationToken ct)
    {
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue || !await HasActionAsync("ADD", ct))
            return Forbid();

        var targetDate = date ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var count = await _patients.SyncEmrAsync(_tenant.RequiredTenantId, targetDate, UserId(), ct);
        return Ok(new { success = true, syncedCount = count, message = $"EMR sync completed for {targetDate:yyyy-MM-dd}." });
    }

    [HttpGet("lookups")]
    public async Task<IActionResult> Lookups(CancellationToken ct)
    {
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue || !await HasActionAsync("VIEW", ct))
            return Forbid();

        var lookups = await _patients.GetPatientLookupsAsync(_tenant.RequiredTenantId, ct);
        return Ok(lookups);
    }
}
