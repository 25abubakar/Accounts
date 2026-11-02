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
[Route("api/billing/receipt-roz")]
public sealed class BillingReceiptRozController : ControllerBase
{
    private const string MenuRoute = "/billing/receipt-roz";
    private readonly ApplicationDbContext _db;
    private readonly ITenantService _tenant;
    private readonly TenantPermissionService _tenantPermissions;
    private readonly RbacService _rbac;
    private readonly IBillingService _billing;
    private readonly IFileStorageService _files;
    private readonly IReferenceGeneratorService _references;

    public BillingReceiptRozController(
        ApplicationDbContext db,
        ITenantService tenant,
        TenantPermissionService tenantPermissions,
        RbacService rbac,
        IBillingService billing,
        IFileStorageService files,
        IReferenceGeneratorService references)
    {
        _db = db;
        _tenant = tenant;
        _tenantPermissions = tenantPermissions;
        _rbac = rbac;
        _billing = billing;
        _files = files;
        _references = references;
    }

    private string? UserId() => User.FindFirstValue(ClaimTypes.NameIdentifier);

    private async Task<bool> HasActionAsync(string action, CancellationToken ct)
    {
        if (TenantPermissionService.IsSuperAdmin(User)) return true;
        var routes = new[] { MenuRoute, "/accounts/receipt-roz" };
        if (TenantPermissionService.IsTenantAdmin(User))
            return await _tenantPermissions.HasMenuRouteAsync(User, routes, action, ct);
        if (!_tenant.TenantId.HasValue) return false;
        var userId = UserId();
        if (string.IsNullOrWhiteSpace(userId)) return false;
        var staffId = await _db.Persons.AsNoTracking()
            .Where(x => x.IdentityUserId == userId && x.Staff != null)
            .Select(x => (Guid?)x.Staff!.StaffId)
            .FirstOrDefaultAsync(ct);
        if (!staffId.HasValue) return false;
        var menuIds = await _db.Menus.AsNoTracking()
            .Where(x => x.IsActive && routes.Contains(x.Route))
            .Select(x => x.Id)
            .ToListAsync(ct);
        if (menuIds.Count == 0) return false;
        var normalized = action.Trim().ToUpperInvariant();
        foreach (var menuId in menuIds)
        {
            if (normalized == "VIEW" && await _rbac.HasAccessAsync(staffId.Value, $"MENU_{menuId}"))
                return true;
            if (await _rbac.HasAccessAsync(staffId.Value, $"MENU_{menuId}_{normalized}"))
                return true;
        }
        return false;
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] DateOnly? dateFrom,
        [FromQuery] DateOnly? dateTo,
        CancellationToken ct)
    {
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue || !await HasActionAsync("VIEW", ct))
            return Forbid();

        var rows = await _billing.ListBillingReceiptRozAsync(_tenant.RequiredTenantId, dateFrom, dateTo, ct);
        return Ok(rows);
    }

    [HttpGet("lookups")]
    public async Task<IActionResult> Lookups(CancellationToken ct)
    {
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue || !await HasActionAsync("VIEW", ct))
            return Forbid();

        var tenantId = _tenant.RequiredTenantId;
        var categories = await _db.AccountsCategories.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.IsActive)
            .OrderBy(x => x.Name)
            .Select(x => new { x.Id, x.Name, x.Code })
            .ToListAsync(ct);

        var accounts = await _db.AccountsChartAccounts.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.IsActive)
            .OrderBy(x => x.AccountName)
            .Select(x => new { x.Id, x.AccountName, x.AccountNumber, x.CategoryId })
            .ToListAsync(ct);

        var statuses = await RoznamchaStatusCatalog.Query(_db, "Receipt")
            .OrderBy(x => x.Name)
            .Select(x => new { x.Id, x.Name, x.ColorCode, x.FontColor })
            .ToListAsync(ct);

        return Ok(new { categories, accounts, statuses });
    }

    [HttpPost("settle")]
    [Idempotent]
    public async Task<IActionResult> Settle([FromBody] BillingSettleRequest request, CancellationToken ct)
    {
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue || !await HasActionAsync("EDIT", ct))
            return Forbid();

        var count = await _billing.SettleReceiptRozAsync(_tenant.RequiredTenantId, request, UserId(), ct);
        return Ok(new { success = true, count, message = $"{count} entry(ies) settled successfully." });
    }

    [HttpPost("unsettle")]
    [Idempotent]
    public async Task<IActionResult> Unsettle([FromBody] List<long> ids, CancellationToken ct)
    {
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue || !await HasActionAsync("EDIT", ct))
            return Forbid();

        var count = await _billing.UnsettleReceiptRozAsync(_tenant.RequiredTenantId, ids, UserId(), ct);
        return Ok(new { success = true, count, message = $"{count} entry(ies) unsettled successfully." });
    }

    [HttpPut("{id:long}/status")]
    public async Task<IActionResult> UpdateStatus(long id, [FromBody] BillingStatusUpdateRequest request, CancellationToken ct)
    {
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue || !await HasActionAsync("EDIT", ct))
            return Forbid();

        var ok = await _billing.UpdateStatusAsync(_tenant.RequiredTenantId, id, request.StatusId, request.Remarks, UserId(), ct);
        return ok ? Ok(new { success = true, message = "Status updated." }) : NotFound(new { success = false, message = "Entry was not found." });
    }

    [HttpPost("transfer-account")]
    [Idempotent]
    public async Task<IActionResult> TransferAccount([FromBody] BillingTransferAccountRequest request, CancellationToken ct)
    {
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue || !await HasActionAsync("EDIT", ct))
            return Forbid();

        var ok = await _billing.TransferAccountAsync(_tenant.RequiredTenantId, request.Id, request.TransferAccountId, UserId(), ct);
        return ok ? Ok(new { success = true, message = "Account transferred successfully." }) : NotFound(new { success = false, message = "Entry was not found." });
    }

    [HttpPost("{id:long}/process-log")]
    [Idempotent]
    public async Task<IActionResult> AddProcessLog(long id, [FromBody] BillingProcessLogRequest request, CancellationToken ct)
    {
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue || !await HasActionAsync("EDIT", ct))
            return Forbid();

        request.EntryId = id;
        var ok = await _billing.AddProcessLogAsync(_tenant.RequiredTenantId, request, UserId(), ct);
        return ok ? Ok(new { success = true, message = "Process log saved." }) : NotFound(new { success = false, message = "Entry was not found." });
    }

    [HttpGet("{id:long}/attachments")]
    public async Task<IActionResult> GetAttachments(long id, CancellationToken ct)
    {
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue || !await HasActionAsync("VIEW", ct))
            return Forbid();

        var docs = await _db.AccountsEntryDocuments.AsNoTracking()
            .Where(x => x.TenantId == _tenant.RequiredTenantId && x.RoznamchaEntryId == id)
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

        return Ok(docs);
    }

    [HttpPost("{id:long}/attachments")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UploadAttachment(long id, [FromForm] UploadAccountsFileRequest request, CancellationToken ct)
    {
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue || !await HasActionAsync("EDIT", ct))
            return Forbid();

        if (!await _db.RoznamchaEntries.AnyAsync(x => x.Id == id && x.TenantId == _tenant.RequiredTenantId, ct))
            return NotFound(new { message = "Receipt entry was not found." });

        if (request.File == null || request.File.Length == 0)
            return BadRequest(new { message = "A valid file is required." });

        var stored = await _files.SaveAccountsFileAsync(_tenant.RequiredTenantId, request.File, ct);
        var doc = new AccountsEntryDocument
        {
            TenantId = _tenant.RequiredTenantId,
            RoznamchaEntryId = id,
            DocumentReference = await _references.DocumentReferenceAsync(_tenant.RequiredTenantId, ct),
            StoredPath = stored.StoredPath,
            FileName = stored.FileName,
            ContentType = stored.ContentType,
            FileSizeBytes = stored.FileSizeBytes,
            Remarks = request.Remarks?.Trim(),
            UploadedByUserId = UserId(),
            UploadedOnUtc = DateTime.UtcNow
        };
        _db.AccountsEntryDocuments.Add(doc);
        await _db.SaveChangesAsync(ct);

        return Ok(new { success = true, message = "Attachment uploaded successfully.", id = doc.Id });
    }
}
