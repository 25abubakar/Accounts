using System.Security.Claims;
using Accounts.Data;
using Accounts.Services.Interfaces;
using Accounts.Services.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Controllers;

[ApiController]
[Route("api/annual-reports")]
[Authorize]
[Produces("application/json")]
public sealed class AnnualReportsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ITenantService _tenant;
    private readonly TenantPermissionService _tenantPermissions;
    private readonly RbacService _rbac;
    private readonly IAnnualReportsService _reports;

    public AnnualReportsController(
        ApplicationDbContext db,
        ITenantService tenant,
        TenantPermissionService tenantPermissions,
        RbacService rbac,
        IAnnualReportsService reports)
    {
        _db = db;
        _tenant = tenant;
        _tenantPermissions = tenantPermissions;
        _rbac = rbac;
        _reports = reports;
    }

    [HttpGet("lookups")]
    public async Task<IActionResult> Lookups(CancellationToken ct)
    {
        if (_tenant.IsSuperAdmin) return Ok(new { types = Array.Empty<object>(), categories = Array.Empty<object>() });
        if (!_tenant.TenantId.HasValue || !await HasAnyViewAsync(ct)) return Forbid();
        return Ok(new
        {
            types = await _reports.ListTypesAsync(ct),
            categories = await _reports.ListCategoriesAsync(_tenant.RequiredTenantId, ct)
        });
    }

    [HttpGet("filters")]
    public async Task<IActionResult> ListFilters(CancellationToken ct)
    {
        if (_tenant.IsSuperAdmin) return Ok(Array.Empty<object>());
        if (!_tenant.TenantId.HasValue || !await HasActionAsync("/annual-reports/filter", "VIEW", ct)) return Forbid();
        return Ok(await _reports.ListFiltersAsync(_tenant.RequiredTenantId, ct));
    }

    [HttpPost("filters")]
    public async Task<IActionResult> SaveFilters([FromBody] SaveAnnualFiltersDto dto, CancellationToken ct)
    {
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue || !await HasActionAsync("/annual-reports/filter", "ADD", ct))
            return Forbid();
        if (dto.ReportTypeId <= 0 || dto.CategoryIds is not { Count: > 0 })
            return BadRequest(new { message = "Select category type and at least one category." });
        try
        {
            await _reports.SaveFiltersAsync(_tenant.RequiredTenantId, dto.ReportTypeId, dto.CategoryIds, UserId(), ct);
            return Ok(await _reports.ListFiltersAsync(_tenant.RequiredTenantId, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("filters/{id:int}/include")]
    public async Task<IActionResult> SetInclude(int id, [FromBody] SetIncludeDto dto, CancellationToken ct)
    {
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue || !await HasActionAsync("/annual-reports/filter", "EDIT", ct))
            return Forbid();
        try
        {
            await _reports.SetFilterIncludeAsync(_tenant.RequiredTenantId, id, dto.IsInclude, UserId(), ct);
            return Ok(new { id, dto.IsInclude });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("filters/{id:int}")]
    public async Task<IActionResult> DeleteFilter(int id, CancellationToken ct)
    {
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue || !await HasActionAsync("/annual-reports/filter", "DELETE", ct))
            return Forbid();
        await _reports.DeleteFilterAsync(_tenant.RequiredTenantId, id, ct);
        return Ok(new { id });
    }

    [HttpGet("matrix")]
    public async Task<IActionResult> Matrix([FromQuery] string kind, [FromQuery] DateOnly? dateFrom, [FromQuery] DateOnly? dateTo, CancellationToken ct)
    {
        var route = KindRoute(kind);
        if (_tenant.IsSuperAdmin) return Ok(new AnnualCategoryMatrixDto());
        if (!_tenant.TenantId.HasValue || !await HasActionAsync(route, "VIEW", ct)) return Forbid();
        var (from, to) = ResolveDates(dateFrom, dateTo);
        return Ok(await _reports.GetCategoryMonthMatrixAsync(_tenant.RequiredTenantId, kind, from, to, ct));
    }

    [HttpGet("month-wise")]
    public async Task<IActionResult> MonthWise([FromQuery] string kind, CancellationToken ct)
    {
        var route = KindRoute(kind);
        if (_tenant.IsSuperAdmin) return Ok(Array.Empty<object>());
        if (!_tenant.TenantId.HasValue || !await HasActionAsync(route, "VIEW", ct)) return Forbid();
        return Ok(await _reports.ListMonthWiseAsync(_tenant.RequiredTenantId, kind, ct));
    }

    [HttpGet("category-wise")]
    public async Task<IActionResult> CategoryWise([FromQuery] string kind, CancellationToken ct)
    {
        var route = KindRoute(kind);
        if (_tenant.IsSuperAdmin) return Ok(Array.Empty<object>());
        if (!_tenant.TenantId.HasValue || !await HasActionAsync(route, "VIEW", ct)) return Forbid();
        return Ok(await _reports.ListCategoryWiseAsync(_tenant.RequiredTenantId, kind, ct));
    }

    [HttpPost("save")]
    public async Task<IActionResult> Save([FromBody] SaveAnnualReportDto dto, CancellationToken ct)
    {
        var route = KindRoute(dto.Kind);
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue || !await HasActionAsync(route, "ADD", ct))
            return Forbid();
        if (dto.DateFrom is null || dto.DateTo is null || dto.DateTo < dto.DateFrom)
            return BadRequest(new { message = "Valid date range is required." });
        var result = await _reports.SaveMonthWiseAsync(
            _tenant.RequiredTenantId,
            dto.Kind,
            dto.DateFrom.Value,
            dto.DateTo.Value,
            dto.Remarks,
            dto.Data ?? [],
            UserId(),
            ct);
        return Ok(new { message = result });
    }

    [HttpPost("headers/{id:long}/approve")]
    public async Task<IActionResult> Approve(long id, [FromBody] ApproveDto dto, CancellationToken ct)
    {
        var route = KindRoute(dto.Kind);
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue || !await HasActionAsync(route, "EDIT", ct))
            return Forbid();
        try
        {
            await _reports.SetApprovedAsync(_tenant.RequiredTenantId, id, dto.IsApproved, UserId(), ct);
            return Ok(new { id, dto.IsApproved });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    private static string KindRoute(string? kind) =>
        string.Equals(kind, "INCOME", StringComparison.OrdinalIgnoreCase)
            ? "/annual-reports/income-report"
            : "/annual-reports/exp-report";

    private static (DateOnly from, DateOnly to) ResolveDates(DateOnly? dateFrom, DateOnly? dateTo)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = dateFrom ?? new DateOnly(today.Year, 1, 1);
        var to = dateTo ?? new DateOnly(today.Year, 12, 31);
        if (to < from) to = from;
        return (from, to);
    }

    private string? UserId() => User.FindFirstValue(ClaimTypes.NameIdentifier);

    private async Task<bool> HasAnyViewAsync(CancellationToken ct) =>
        await HasActionAsync("/annual-reports/filter", "VIEW", ct)
        || await HasActionAsync("/annual-reports/exp-report", "VIEW", ct)
        || await HasActionAsync("/annual-reports/income-report", "VIEW", ct);

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
}

public sealed class SaveAnnualFiltersDto
{
    public int ReportTypeId { get; set; }
    public List<int> CategoryIds { get; set; } = [];
}

public sealed class SetIncludeDto
{
    public bool IsInclude { get; set; }
}

public sealed class SaveAnnualReportDto
{
    public string Kind { get; set; } = "EXPENSE";
    public DateOnly? DateFrom { get; set; }
    public DateOnly? DateTo { get; set; }
    public string? Remarks { get; set; }
    public List<AnnualMonthSaveLineDto>? Data { get; set; }
}

public sealed class ApproveDto
{
    public string Kind { get; set; } = "EXPENSE";
    public bool IsApproved { get; set; }
}
