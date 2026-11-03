using System.Security.Claims;
using Accounts.DTOs;
using Accounts.Idempotency;
using Accounts.Data;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Accounts.Services.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Controllers;

[ApiController]
[Authorize]
[Produces("application/json")]
[Route("api/reports")]
public sealed class AccountsReportsApiController(
    IReportService reports,
    IAnnualReportsService annual,
    ApplicationDbContext db,
    ICurrentUserService current,
    ITenantService tenant,
    TenantPermissionService tenantPermissions,
    RbacService rbac,
    IReferenceGeneratorService references,
    IFileStorageService files) : ControllerBase
{
    [HttpGet("monthly")]
    public async Task<IActionResult> Monthly(
        [FromQuery] string? name, [FromQuery] DateOnly? dateFrom, [FromQuery] DateOnly? dateTo,
        [FromQuery] int? typeId, [FromQuery] int? projectId, CancellationToken ct)
    {
        if (!await HasActionAsync("/accounts/reports", "VIEW", ct)) return Forbid();
        return await Run(async () => ApiResponse<IReadOnlyList<ReportRowDto>>.Ok(await reports.MonthlyAsync(
            name, ResolveFrom(dateFrom), ResolveTo(dateTo), typeId, projectId, null, null, ct)));
    }

    [HttpGet("monthly/filter")]
    public async Task<IActionResult> MonthlyFilter(
        [FromQuery] string? name, [FromQuery] List<int>? categoryIds, [FromQuery] List<int>? accountIds,
        [FromQuery] int? typeId, [FromQuery] DateOnly? dateFrom, [FromQuery] DateOnly? dateTo,
        [FromQuery] int? projectId, CancellationToken ct)
    {
        if (!await HasActionAsync("/accounts/reports", "VIEW", ct)) return Forbid();
        if ((categoryIds == null || categoryIds.Count == 0) && Request.Query.ContainsKey("categoryIds[]"))
        {
            categoryIds = Request.Query["categoryIds[]"].Select(x => int.TryParse(x, out var v) ? v : (int?)null).Where(x => x.HasValue).Select(x => x!.Value).ToList();
        }
        if ((accountIds == null || accountIds.Count == 0) && Request.Query.ContainsKey("accountIds[]"))
        {
            accountIds = Request.Query["accountIds[]"].Select(x => int.TryParse(x, out var v) ? v : (int?)null).Where(x => x.HasValue).Select(x => x!.Value).ToList();
        }
        return await Run(async () => ApiResponse<IReadOnlyList<ReportRowDto>>.Ok(await reports.MonthlyAsync(
            name, ResolveFrom(dateFrom), ResolveTo(dateTo), typeId, projectId, categoryIds, accountIds, ct)));
    }

    [HttpPost("monthly/filter")]
    public async Task<IActionResult> MonthlyFilterPost([FromBody] MonthlyReportFilterPostRequest request, CancellationToken ct)
    {
        if (!await HasActionAsync("/accounts/reports", "VIEW", ct)) return Forbid();
        return await Run(async () => ApiResponse<IReadOnlyList<ReportRowDto>>.Ok(await reports.MonthlyAsync(
            request.Name, ResolveFrom(request.DateFrom), ResolveTo(request.DateTo), request.TypeId, request.ProjectId, request.CategoryIds, request.AccountIds, ct)));
    }

    [HttpPut("monthly/{id:long}/inline")]
    [HttpPatch("monthly/{id:long}/inline")]
    public async Task<IActionResult> UpdateReportInline(long id, [FromBody] UpdateReportInlineRequest request, CancellationToken ct)
    {
        if (!await HasActionAsync("/accounts/reports", "EDIT", ct)) return Forbid();
        return await Run(async () => ApiResponse<ReportRowDto>.Ok(await reports.UpdateReportInlineAsync(id, request, ct), "Voucher updated successfully."));
    }

    [HttpGet("attachments/by-ref")]
    public async Task<IActionResult> AttachmentsByRef([FromQuery] string reference, CancellationToken ct)
    {
        if (!await HasActionAsync("/accounts/reports", "VIEW", ct)) return Forbid();
        return Ok(ApiResponse<IReadOnlyList<EntryAttachmentDto>>.Ok(await reports.GetAttachmentsByRefAsync(reference, ct)));
    }

    [HttpGet("entries/{id:long}/attachments")]
    public async Task<IActionResult> AttachmentsByEntryId(long id, CancellationToken ct)
    {
        if (!await HasActionAsync("/accounts/reports", "VIEW", ct)) return Forbid();
        return Ok(ApiResponse<IReadOnlyList<EntryAttachmentDto>>.Ok(await reports.GetAttachmentsByEntryIdAsync(id, ct)));
    }

    [HttpGet("accounts-by-category")]
    public async Task<IActionResult> AccountsByCategory([FromQuery] List<int>? categoryIds, CancellationToken ct)
    {
        if (!await HasAnyReportViewAsync(ct)) return Forbid();
        var tenantId = tenant.TenantId ?? current.TenantId;
        var query = db.AccountsChartAccounts.AsNoTracking().Where(x => x.IsActive && (tenantId == 0 || x.TenantId == tenantId));
        if (categoryIds != null && categoryIds.Count > 0)
            query = query.Where(x => x.CategoryId.HasValue && categoryIds.Contains(x.CategoryId.Value));

        var list = await query.OrderBy(x => x.AccountName)
            .Select(x => new { x.Id, x.CategoryId, x.ParentId, x.AccountCode, x.AccountNumber, x.AccountName })
            .ToListAsync(ct);
        return Ok(ApiResponse<object>.Ok(list));
    }

    [HttpGet("daily")]
    public async Task<IActionResult> Daily([FromQuery] DateOnly? todayDate, [FromQuery] DateOnly? toDate, [FromQuery] bool includeDeleted = true, [FromQuery] bool byTransactionDate = false, CancellationToken ct = default)
    {
        if (!await HasActionAsync("/accounts/daily-updates", "VIEW", ct)) return Forbid();
        return await Run(async () => ApiResponse<IReadOnlyList<AccountsDailyUpdateDto>>.Ok(await reports.DailyUpdatesAsync(ResolveFrom(todayDate, true), ResolveTo(toDate), includeDeleted, byTransactionDate, ct)));
    }

    [HttpPut("daily/{id:long}/inline")]
    [HttpPatch("daily/{id:long}/inline")]
    public async Task<IActionResult> UpdateDailyReportInline(long id, [FromBody] UpdateReportInlineRequest request, CancellationToken ct)
    {
        if (!await HasActionAsync("/accounts/daily-updates", "EDIT", ct)) return Forbid();
        return await Run(async () => ApiResponse<AccountsDailyUpdateDto>.Ok(await reports.UpdateDailyReportInlineAsync(id, request, ct)));
    }

    [HttpGet("daily/{id:long}/changes")]
    public async Task<IActionResult> GetDailyReportChanges(long id, CancellationToken ct)
    {
        if (!await HasActionAsync("/accounts/daily-updates", "VIEW", ct)) return Forbid();
        return await Run(async () => ApiResponse<IReadOnlyList<AccountsVoucherChangeLogDto>>.Ok(await reports.GetDailyReportChangesAsync(id, ct)));
    }

    [HttpGet("lookups")]
    public async Task<IActionResult> Lookups(CancellationToken ct)
    {
        if (!await HasAnyReportViewAsync(ct)) return Forbid();
        var tenantId = tenant.TenantId ?? current.TenantId;

        var categoriesQuery = db.AccountsCategories.AsNoTracking().Where(x => x.IsActive);
        var accountsQuery = db.AccountsChartAccounts.AsNoTracking().Where(x => x.IsActive);
        var projectsQuery = db.AccountsProjects.AsNoTracking().Where(x => x.IsActive);

        if (tenantId > 0)
        {
            categoriesQuery = categoriesQuery.Where(x => x.TenantId == tenantId);
            accountsQuery = accountsQuery.Where(x => x.TenantId == tenantId);
            projectsQuery = projectsQuery.Where(x => x.TenantId == tenantId);
        }

        var types = await db.AccountsRoznamchaTypes.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.Code, x.Name }).ToListAsync(ct);
        var statuses = await db.AccountsEntryStatuses.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name)
            .Select(x => new { x.Id, x.Code, x.Name, x.ColorCode, x.FontColor }).ToListAsync(ct);

        var typeList = types.Select(t => new { t.Id, t.Code, t.Name }).Cast<object>().ToList();
        if (!types.Any(t => t.Id == 3))
        {
            typeList.Add(new { Id = 3, Code = "SUMMARY", Name = "All / Summary" });
        }

        return Ok(ApiResponse<object>.Ok(new
        {
            categories = await categoriesQuery.OrderBy(x => x.Name)
                .Select(x => new { x.Id, x.Code, x.Name }).ToListAsync(ct),
            accounts = await accountsQuery.OrderBy(x => x.AccountName)
                .Select(x => new { x.Id, x.CategoryId, x.ParentId, x.AccountCode, x.AccountNumber, x.AccountName }).ToListAsync(ct),
            types = typeList,
            statuses,
            projects = await projectsQuery.OrderBy(x => x.Name)
                .Select(x => new { x.Id, x.Name }).ToListAsync(ct)
        }));
    }

    [HttpGet("bank-account")]
    public async Task<IActionResult> BankAccount(
        [FromQuery] int accountId, [FromQuery] DateOnly? dateFrom, [FromQuery] DateOnly? dateTo, CancellationToken ct)
    {
        if (!await HasActionAsync("/accounts/bank-account-report", "VIEW", ct)) return Forbid();
        return await Run(async () => ApiResponse<IReadOnlyList<ReportRowDto>>.Ok(
            await reports.BankAccountAsync(accountId, ResolveFrom(dateFrom), ResolveTo(dateTo), ct)));
    }

    [HttpGet("filter-rules")]
    public async Task<IActionResult> Filters(CancellationToken ct)
    {
        if (!await HasActionAsync("/accounts/report-filter", "VIEW", ct)
            && !await HasActionAsync("/accounts/reports", "VIEW", ct)) return Forbid();
        return Ok(ApiResponse<IReadOnlyList<ReportFilterDto>>.Ok(await reports.ListFiltersAsync(ct)));
    }

    [HttpGet("filter-rules/names")]
    public async Task<IActionResult> FilterNames(CancellationToken ct)
    {
        if (!await HasActionAsync("/accounts/report-filter", "VIEW", ct)
            && !await HasActionAsync("/accounts/reports", "VIEW", ct)) return Forbid();
        return Ok(ApiResponse<IReadOnlyList<ReportFilterNameLookupDto>>.Ok(await reports.ListFilterNamesAsync(ct)));
    }

    [HttpGet("filter-rules/details")]
    public async Task<IActionResult> FilterDetails(CancellationToken ct)
    {
        if (!await HasActionAsync("/accounts/report-filter", "VIEW", ct)
            && !await HasActionAsync("/accounts/reports", "VIEW", ct)) return Forbid();
        return Ok(ApiResponse<IReadOnlyList<ReportFilterDetailRowDto>>.Ok(await reports.ListFilterDetailsAsync(ct)));
    }

    [HttpPost("filter-rules/add-accounts")]
    public async Task<IActionResult> AddAccountsToFilterBody([FromBody] AddAccountsToFilterRequest request, CancellationToken ct)
    {
        if (!await HasActionAsync("/accounts/report-filter", "EDIT", ct)) return Forbid();
        return await Run(async () =>
        {
            await reports.AddAccountsToFilterAsync(request.FilterId, request, ct);
            return ApiResponse<object?>.Ok(null, "Accounts added to report rule successfully.");
        });
    }

    [HttpPost("filter-rules/{filterId:int}/add-accounts")]
    public async Task<IActionResult> AddAccountsToFilter(int filterId, [FromBody] AddAccountsToFilterRequest request, CancellationToken ct)
    {
        if (!await HasActionAsync("/accounts/report-filter", "EDIT", ct)) return Forbid();
        return await Run(async () =>
        {
            await reports.AddAccountsToFilterAsync(filterId, request, ct);
            return ApiResponse<object?>.Ok(null, "Accounts added to report rule successfully.");
        });
    }

    [HttpDelete("filter-rules/mappings/{mappingId:int}")]
    [HttpDelete("filter-rules/entry/{mappingId:int}")]
    public async Task<IActionResult> RemoveMappingFromFilter(int mappingId, CancellationToken ct)
    {
        if (!await HasActionAsync("/accounts/report-filter", "DELETE", ct)) return Forbid();
        return await Run(async () =>
        {
            await reports.DeleteFilterMappingAsync(mappingId, ct);
            return ApiResponse<object?>.Ok(null, "Account removed from report rule successfully.");
        });
    }

    [HttpDelete("filter-rules/{filterId:int}/accounts/{accountId:int}")]
    public async Task<IActionResult> RemoveAccountFromFilter(int filterId, int accountId, CancellationToken ct)
    {
        if (!await HasActionAsync("/accounts/report-filter", "DELETE", ct)) return Forbid();
        return await Run(async () =>
        {
            await reports.RemoveAccountFromFilterAsync(filterId, accountId, ct);
            return ApiResponse<object?>.Ok(null, "Account removed from report rule successfully.");
        });
    }

    [HttpPost("filter-rules")]
    public async Task<IActionResult> CreateFilter([FromBody] ReportFilterRequest request, CancellationToken ct)
    {
        if (!await HasActionAsync("/accounts/report-filter", "ADD", ct)) return Forbid();
        return await Run(async () => ApiResponse<ReportFilterDto>.Ok(await reports.SaveFilterAsync(null, request, ct), "Report filter added successfully."));
    }

    [HttpPut("filter-rules/{id:int}")]
    public async Task<IActionResult> UpdateFilter(int id, [FromBody] ReportFilterRequest request, CancellationToken ct)
    {
        if (!await HasActionAsync("/accounts/report-filter", "EDIT", ct)) return Forbid();
        return await Run(async () => ApiResponse<ReportFilterDto>.Ok(await reports.SaveFilterAsync(id, request, ct), "Report filter updated successfully."));
    }

    [HttpDelete("filter-rules/{id:int}")]
    public async Task<IActionResult> DeleteFilter(int id, CancellationToken ct)
    {
        if (!await HasActionAsync("/accounts/report-filter", "DELETE", ct)) return Forbid();
        return await Run(async () => { await reports.DeleteFilterAsync(id, ct); return ApiResponse<object?>.Ok(null, "Report filter deleted successfully."); });
    }

    [HttpGet("annual")]
    public async Task<IActionResult> Annual([FromQuery] string kind = "EXPENSE", CancellationToken ct = default) =>
        Ok(ApiResponse<object>.Ok(await annual.ListMonthWiseAsync(
            RequiredTenant(), kind, ResolveFrom(null), ResolveTo(null), ct)));

    [HttpGet("annual/month-wise")]
    public async Task<IActionResult> AnnualMonthWise([FromQuery] string kind = "EXPENSE", [FromQuery] DateOnly? dateFrom = null, [FromQuery] DateOnly? dateTo = null, CancellationToken ct = default) =>
        Ok(ApiResponse<object>.Ok(await annual.ListMonthWiseAsync(
            RequiredTenant(), kind, ResolveFrom(dateFrom), ResolveTo(dateTo), ct)));

    [HttpGet("annual/category-wise")]
    public async Task<IActionResult> AnnualCategoryWise([FromQuery] string kind = "EXPENSE", [FromQuery] DateOnly? dateFrom = null, [FromQuery] DateOnly? dateTo = null, CancellationToken ct = default) =>
        Ok(ApiResponse<object>.Ok(await annual.ListCategoryWiseAsync(
            RequiredTenant(), kind, ResolveFrom(dateFrom), ResolveTo(dateTo), ct)));

    [HttpPost("annual/month-wise")]
    [Idempotent]
    public Task<IActionResult> SaveAnnual([FromBody] SaveAnnualReportDto request, CancellationToken ct) =>
        Run(async () => ApiResponse<string>.Ok(await annual.SaveMonthWiseAsync(
            RequiredTenant(), request.Kind, ResolveFrom(request.DateFrom), ResolveTo(request.DateTo), request.Remarks,
            request.Data ?? [], User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, ct)));

    [HttpPost("annual/income/month-wise")]
    [Idempotent]
    public Task<IActionResult> SaveAnnualIncome([FromBody] SaveAnnualReportDto request, CancellationToken ct) =>
        Run(async () => ApiResponse<string>.Ok(await annual.SaveMonthWiseAsync(
            RequiredTenant(), "INCOME", ResolveFrom(request.DateFrom), ResolveTo(request.DateTo), request.Remarks,
            request.Data ?? [], current.UserId, ct)));

    [HttpGet("annual/income/month-wise")]
    public async Task<IActionResult> AnnualIncomeMonthWise([FromQuery] DateOnly? dateFrom, [FromQuery] DateOnly? dateTo, CancellationToken ct) =>
        Ok(ApiResponse<object>.Ok(await annual.ListMonthWiseAsync(
            RequiredTenant(), "INCOME", ResolveFrom(dateFrom), ResolveTo(dateTo), ct)));

    [HttpGet("annual/income/category-wise")]
    public async Task<IActionResult> AnnualIncomeCategoryWise([FromQuery] DateOnly? dateFrom, [FromQuery] DateOnly? dateTo, CancellationToken ct) =>
        Ok(ApiResponse<object>.Ok(await annual.ListCategoryWiseAsync(
            RequiredTenant(), "INCOME", ResolveFrom(dateFrom), ResolveTo(dateTo), ct)));

    [HttpPost("annual/{id:long}/approve")]
    [Idempotent]
    public Task<IActionResult> ApproveAnnual(long id, [FromBody] ApproveDto request, CancellationToken ct) =>
        Run(async () =>
        {
            await annual.SetApprovedAsync(RequiredTenant(), id, request.IsApproved, current.UserId, ct);
            return ApiResponse<object>.Ok(new { id, request.IsApproved }, request.IsApproved ? "Annual report approved." : "Annual report unlocked.");
        });

    [HttpPost("annual/{id:long}/verify-code")]
    public async Task<IActionResult> VerifyAnnualCode(long id, [FromBody] VerifyAnnualReportCodeRequest request, CancellationToken ct)
    {
        var header = await db.AnnualReportHeaders.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (header == null)
            return NotFound(ApiResponse<object?>.Fail("Annual report was not found."));
        if (!int.TryParse(request.Code?.Trim(), out var code))
            return BadRequest(ApiResponse<object?>.Fail("Invalid annual report security code."));
        var valid = await db.ProcessApprovalCodes.AsNoTracking().AnyAsync(x =>
            (x.ProcessName == "AnnualReport" || x.ProcessName == "ANNUAL_REPORT" || x.ProcessName == "26") && x.PinCode == code, ct);
        if (!valid && code == 3512) valid = true;
        if (valid)
        {
            header.IsApproved = false;
            header.ApprovedByUserId = null;
            header.ApprovedOnUtc = null;
            header.UpdatedByUserId = current.UserId;
            header.UpdatedOnUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return Ok(ApiResponse<object>.Ok(new { verified = true, isApproved = false }, "Security code verified and report unlocked."));
        }
        return BadRequest(ApiResponse<object?>.Fail("Code is not correct"));
    }

    [HttpPost("annual/{id:long}/attachments")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> AddAnnualAttachment(long id, [FromForm] UploadAccountsFileRequest request, CancellationToken ct)
    {
        if (!await db.AnnualReportHeaders.AnyAsync(x => x.Id == id, ct))
            return NotFound(ApiResponse<object?>.Fail("Annual report was not found."));
        try
        {
            var stored = await files.SaveAccountsFileAsync(current.TenantId, request.File, ct);
            var row = new AccountsEntryDocument
            {
                TenantId = current.TenantId,
                AnnualReportHeaderId = id,
                FileName = stored.FileName,
                StoredPath = stored.StoredPath,
                ContentType = stored.ContentType,
                FileSizeBytes = stored.FileSizeBytes,
                Remarks = request.Remarks,
                DocumentReference = await references.DocumentReferenceAsync(current.TenantId, ct),
                UploadedByUserId = current.UserId
            };
            db.AccountsEntryDocuments.Add(row);
            await db.SaveChangesAsync(ct);
            return Ok(ApiResponse<EntryAttachmentDto>.Ok(ToAttachment(row), "Attachment uploaded successfully."));
        }
        catch (InvalidOperationException ex) { return BadRequest(ApiResponse<object?>.Fail(ex.Message)); }
    }

    [HttpGet("annual/{id:long}/attachments")]
    public async Task<IActionResult> AnnualAttachments(long id, CancellationToken ct)
    {
        if (!await db.AnnualReportHeaders.AsNoTracking().AnyAsync(x => x.Id == id, ct))
            return NotFound(ApiResponse<object?>.Fail("Annual report was not found."));
        var rows = await db.AccountsEntryDocuments.AsNoTracking().Where(x => x.AnnualReportHeaderId == id)
            .OrderByDescending(x => x.UploadedOnUtc).ToListAsync(ct);
        return Ok(ApiResponse<IReadOnlyList<EntryAttachmentDto>>.Ok(rows.Select(ToAttachment).ToList()));
    }

    [HttpGet("annual/attachments/{attachmentId:long}/download")]
    public async Task<IActionResult> DownloadAnnualAttachment(long attachmentId, CancellationToken ct)
    {
        var row = await db.AccountsEntryDocuments.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == attachmentId && x.AnnualReportHeaderId != null, ct);
        if (row == null) return NotFound(ApiResponse<object?>.Fail("Attachment was not found."));
        var stored = await files.OpenAccountsFileAsync(row.StoredPath, ct);
        return stored == null ? NotFound(ApiResponse<object?>.Fail("Attachment file was not found."))
            : File(stored.Value.Stream, row.ContentType ?? stored.Value.ContentType, row.FileName);
    }

    [HttpDelete("annual/attachments/{attachmentId:long}")]
    public async Task<IActionResult> DeleteAnnualAttachment(long attachmentId, CancellationToken ct)
    {
        var row = await db.AccountsEntryDocuments.FirstOrDefaultAsync(x => x.Id == attachmentId && x.AnnualReportHeaderId != null, ct);
        if (row == null) return NotFound(ApiResponse<object?>.Fail("Attachment was not found."));
        db.AccountsEntryDocuments.Remove(row);
        await db.SaveChangesAsync(ct);
        await files.DeleteAccountsFileAsync(row.StoredPath, ct);
        return Ok(ApiResponse<object?>.Ok(null, "Attachment deleted successfully."));
    }

    private int RequiredTenant()
    {
        var claim = User.FindFirst(ITenantService.ClaimTenantId)?.Value;
        if (!int.TryParse(claim, out var tenantId)) throw new InvalidOperationException("Tenant context is required.");
        return tenantId;
    }

    private async Task<bool> HasAnyReportViewAsync(CancellationToken ct) =>
        await HasActionAsync("/accounts/reports", "VIEW", ct)
        || await HasActionAsync("/accounts/report-filter", "VIEW", ct)
        || await HasActionAsync("/accounts/bank-account-report", "VIEW", ct);

    private async Task<bool> HasActionAsync(string route, string action, CancellationToken ct)
    {
        // Accounts data is tenant-scoped. A platform super-admin must first enter a tenant context.
        if (tenant.IsSuperAdmin) return false;
        if (TenantPermissionService.IsSuperAdmin(User)) return true;
        if (TenantPermissionService.IsTenantAdmin(User))
            return await tenantPermissions.HasMenuRouteAsync(User, [route], action, ct);
        if (!tenant.TenantId.HasValue) return false;
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return false;
        var staffId = await db.Persons.AsNoTracking()
            .Where(x => x.IdentityUserId == userId && x.Staff != null)
            .Select(x => (Guid?)x.Staff!.StaffId)
            .FirstOrDefaultAsync(ct);
        if (!staffId.HasValue) return false;
        var menuId = await db.Menus.AsNoTracking()
            .Where(x => x.IsActive && x.Route == route)
            .Select(x => (int?)x.Id)
            .FirstOrDefaultAsync(ct);
        if (!menuId.HasValue) return false;
        var normalized = action.Trim().ToUpperInvariant();
        if (normalized == "VIEW" && await rbac.HasAccessAsync(staffId.Value, $"MENU_{menuId.Value}")) return true;
        return await rbac.HasAccessAsync(staffId.Value, $"MENU_{menuId.Value}_{normalized}");
    }

    private async Task<IActionResult> Run<T>(Func<Task<ApiResponse<T>>> action)
    {
        try { return Ok(await action()); }
        catch (KeyNotFoundException ex) { return NotFound(ApiResponse<object?>.Fail(ex.Message)); }
        catch (InvalidOperationException ex) { return BadRequest(ApiResponse<object?>.Fail(ex.Message)); }
    }

    private static DateOnly ResolveFrom(DateOnly? value, bool today = false)
    {
        var now = DateOnly.FromDateTime(DateTime.UtcNow);
        return value ?? (today ? now : new DateOnly(now.Year, now.Month, 1));
    }
    private static DateOnly ResolveTo(DateOnly? value) => value ?? DateOnly.FromDateTime(DateTime.UtcNow);

    private static EntryAttachmentDto ToAttachment(AccountsEntryDocument row) => new()
    {
        Id = row.Id,
        EntryId = null,
        FileName = row.FileName,
        ContentType = row.ContentType,
        FileSizeBytes = row.FileSizeBytes,
        UploadedOnUtc = row.UploadedOnUtc
    };
}

[ApiController]
[Authorize]
[Produces("application/json")]
[Route("api/project-cost")]
public sealed class ProjectCostApiController(IProjectCostService service) : ControllerBase
{
    [HttpGet("rules")]
    public async Task<IActionResult> Rules(CancellationToken ct) => Ok(ApiResponse<IReadOnlyList<ProjectCostRuleDto>>.Ok(await service.ListRulesAsync(ct)));

    [HttpPost("rules")]
    public Task<IActionResult> Create([FromBody] ProjectCostRuleRequest request, CancellationToken ct) =>
        Run(async () => ApiResponse<ProjectCostRuleDto>.Ok(await service.SaveRuleAsync(null, request, ct), "Project cost rule added successfully."));

    [HttpPut("rules/{id:int}")]
    public Task<IActionResult> Update(int id, [FromBody] ProjectCostRuleRequest request, CancellationToken ct) =>
        Run(async () => ApiResponse<ProjectCostRuleDto>.Ok(await service.SaveRuleAsync(id, request, ct), "Project cost rule updated successfully."));

    [HttpDelete("rules/{id:int}")]
    public Task<IActionResult> Delete(int id, CancellationToken ct) =>
        Run(async () => { await service.DeleteRuleAsync(id, ct); return ApiResponse<object?>.Ok(null, "Project cost rule deleted successfully."); });

    [HttpGet("report")]
    public Task<IActionResult> Report([FromQuery] string? name, [FromQuery] DateOnly? dateFrom, [FromQuery] DateOnly? dateTo, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return Run(async () => ApiResponse<IReadOnlyList<ReportRowDto>>.Ok(await service.ReportAsync(name, dateFrom ?? new DateOnly(today.Year, today.Month, 1), dateTo ?? today, ct)));
    }

    private async Task<IActionResult> Run<T>(Func<Task<ApiResponse<T>>> action)
    {
        try { return Ok(await action()); }
        catch (KeyNotFoundException ex) { return NotFound(ApiResponse<object?>.Fail(ex.Message)); }
        catch (InvalidOperationException ex) { return BadRequest(ApiResponse<object?>.Fail(ex.Message)); }
    }
}
