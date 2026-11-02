using System.Security.Claims;
using Accounts.Data;
using Accounts.DTOs;
using Accounts.Models;
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
    private readonly IFileStorageService _files;
    private readonly IReferenceGeneratorService _references;

    public AnnualReportsController(
        ApplicationDbContext db,
        ITenantService tenant,
        TenantPermissionService tenantPermissions,
        RbacService rbac,
        IAnnualReportsService reports,
        IFileStorageService files,
        IReferenceGeneratorService references)
    {
        _db = db;
        _tenant = tenant;
        _tenantPermissions = tenantPermissions;
        _rbac = rbac;
        _reports = reports;
        _files = files;
        _references = references;
    }

    [HttpGet("lookups")]
    public async Task<IActionResult> Lookups(CancellationToken ct)
    {
        var types = await _reports.ListTypesAsync(ct);
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue || !await HasAnyViewAsync(ct))
            return Ok(new { types, categories = Array.Empty<object>() });

        return Ok(new
        {
            types,
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
    public async Task<IActionResult> MonthWise(
        [FromQuery] string kind, [FromQuery] DateOnly? dateFrom, [FromQuery] DateOnly? dateTo, CancellationToken ct)
    {
        var route = KindRoute(kind);
        if (_tenant.IsSuperAdmin) return Ok(Array.Empty<object>());
        if (!_tenant.TenantId.HasValue || !await HasActionAsync(route, "VIEW", ct)) return Forbid();
        var (from, to) = ResolveDates(dateFrom, dateTo);
        return Ok(await _reports.ListMonthWiseAsync(_tenant.RequiredTenantId, kind, from, to, ct));
    }

    [HttpGet("category-wise")]
    public async Task<IActionResult> CategoryWise(
        [FromQuery] string kind, [FromQuery] DateOnly? dateFrom, [FromQuery] DateOnly? dateTo, CancellationToken ct)
    {
        var route = KindRoute(kind);
        if (_tenant.IsSuperAdmin) return Ok(Array.Empty<object>());
        if (!_tenant.TenantId.HasValue || !await HasActionAsync(route, "VIEW", ct)) return Forbid();
        var (from, to) = ResolveDates(dateFrom, dateTo);
        return Ok(await _reports.ListCategoryWiseAsync(_tenant.RequiredTenantId, kind, from, to, ct));
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

    [HttpPost("headers/{id:long}/verify-unlock")]
    public async Task<IActionResult> VerifyUnlock(long id, [FromBody] VerifyAnnualCodeDto dto, CancellationToken ct)
    {
        var route = KindRoute(dto.Kind);
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue || !await HasActionAsync(route, "EDIT", ct))
            return Forbid();
        if (string.IsNullOrWhiteSpace(dto.Code))
            return BadRequest(new { success = false, message = "Security code is required." });
        var verified = await _reports.VerifyUnlockAsync(_tenant.RequiredTenantId, id, dto.Code, UserId(), ct);
        if (verified)
            return Ok(new { success = true, isApproved = false, message = "Report unlocked successfully." });
        return BadRequest(new { success = false, message = "Code is not correct" });
    }

    [HttpPost("headers/{id:long}/verify-code")]
    public Task<IActionResult> VerifyCodeAlias(long id, [FromBody] VerifyAnnualCodeDto dto, CancellationToken ct) =>
        VerifyUnlock(id, dto, ct);

    [HttpGet("headers/{id:long}/attachments")]
    public async Task<IActionResult> GetAttachments(long id, CancellationToken ct)
    {
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue || !await HasAnyViewAsync(ct)) return Forbid();
        if (!await _db.AnnualReportHeaders.AsNoTracking().AnyAsync(x => x.Id == id && x.TenantId == _tenant.RequiredTenantId, ct))
            return NotFound(new { message = "Annual report was not found." });
        var rows = await _db.AccountsEntryDocuments.AsNoTracking()
            .Where(x => x.TenantId == _tenant.RequiredTenantId && x.AnnualReportHeaderId == id)
            .OrderByDescending(x => x.UploadedOnUtc)
            .Select(x => new
            {
                x.Id,
                x.FileName,
                x.ContentType,
                x.FileSizeBytes,
                x.Remarks,
                x.DocumentReference,
                x.UploadedOnUtc,
                x.UploadedByUserId
            })
            .ToListAsync(ct);
        return Ok(rows);
    }

    [HttpPost("headers/{id:long}/attachments")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UploadAttachment(long id, [FromForm] UploadAccountsFileRequest request, CancellationToken ct)
    {
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue || !await HasAnyViewAsync(ct)) return Forbid();
        if (!await _db.AnnualReportHeaders.AnyAsync(x => x.Id == id && x.TenantId == _tenant.RequiredTenantId, ct))
            return NotFound(new { message = "Annual report was not found." });
        if (request.File == null || request.File.Length == 0)
            return BadRequest(new { message = "A valid file is required." });
        try
        {
            var stored = await _files.SaveAccountsFileAsync(_tenant.RequiredTenantId, request.File, ct);
            var row = new AccountsEntryDocument
            {
                TenantId = _tenant.RequiredTenantId,
                AnnualReportHeaderId = id,
                FileName = stored.FileName,
                StoredPath = stored.StoredPath,
                ContentType = stored.ContentType,
                FileSizeBytes = stored.FileSizeBytes,
                Remarks = request.Remarks,
                DocumentReference = await _references.DocumentReferenceAsync(_tenant.RequiredTenantId, ct),
                UploadedByUserId = UserId()
            };
            _db.AccountsEntryDocuments.Add(row);
            await _db.SaveChangesAsync(ct);
            return Ok(new
            {
                row.Id,
                row.FileName,
                row.ContentType,
                row.FileSizeBytes,
                row.Remarks,
                row.DocumentReference,
                row.UploadedOnUtc
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("attachments/{attachmentId:long}/download")]
    public async Task<IActionResult> DownloadAttachment(long attachmentId, CancellationToken ct)
    {
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue || !await HasAnyViewAsync(ct)) return Forbid();
        var row = await _db.AccountsEntryDocuments.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == attachmentId && x.TenantId == _tenant.RequiredTenantId && x.AnnualReportHeaderId != null, ct);
        if (row == null) return NotFound(new { message = "Attachment was not found." });
        var stored = await _files.OpenAccountsFileAsync(row.StoredPath, ct);
        if (stored == null) return NotFound(new { message = "Attachment file was not found." });
        return File(stored.Value.Stream, row.ContentType ?? stored.Value.ContentType, row.FileName);
    }

    [HttpDelete("attachments/{attachmentId:long}")]
    public async Task<IActionResult> DeleteAttachment(long attachmentId, CancellationToken ct)
    {
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue || !await HasAnyViewAsync(ct)) return Forbid();
        var row = await _db.AccountsEntryDocuments
            .FirstOrDefaultAsync(x => x.Id == attachmentId && x.TenantId == _tenant.RequiredTenantId && x.AnnualReportHeaderId != null, ct);
        if (row == null) return NotFound(new { message = "Attachment was not found." });
        _db.AccountsEntryDocuments.Remove(row);
        await _db.SaveChangesAsync(ct);
        await _files.DeleteAccountsFileAsync(row.StoredPath, ct);
        return Ok(new { message = "Attachment deleted successfully." });
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

public sealed class VerifyAnnualCodeDto
{
    public string Kind { get; set; } = "EXPENSE";
    public string Code { get; set; } = string.Empty;
}
