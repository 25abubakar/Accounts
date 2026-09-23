using System.Security.Claims;
using Accounts.Data;
using Accounts.DTOs;
using Accounts.Idempotency;
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
    private const string ReceiptRozRoute = "/accounts/receipt-roz";

    private readonly ApplicationDbContext _db;
    private readonly ITenantService _tenant;
    private readonly TenantPermissionService _tenantPermissions;
    private readonly RbacService _rbac;
    private readonly IAccountsRoznamchaService _roznamcha;
    private readonly IRoznamchaService _transactions;

    public AccountsRoznamchaController(
        ApplicationDbContext db,
        ITenantService tenant,
        TenantPermissionService tenantPermissions,
        RbacService rbac,
        IAccountsRoznamchaService roznamcha,
        IRoznamchaService transactions)
    {
        _db = db;
        _tenant = tenant;
        _tenantPermissions = tenantPermissions;
        _rbac = rbac;
        _roznamcha = roznamcha;
        _transactions = transactions;
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

    [HttpGet("payment-roz/lookups")]
    public async Task<IActionResult> PaymentRozLookups(CancellationToken ct)
    {
        if (_tenant.IsSuperAdmin) return Ok(new { categories = Array.Empty<object>(), accounts = Array.Empty<object>() });
        if (!_tenant.TenantId.HasValue || !await HasActionAsync(PaymentRozRoute, "VIEW", ct)) return Forbid();

        return Ok(await LoadRoznamchaLookupsAsync(ct));
    }

    [HttpGet("receipt-roz")]
    public async Task<IActionResult> ListReceiptRoz(
        [FromQuery] DateOnly? dateFrom,
        [FromQuery] DateOnly? dateTo,
        CancellationToken ct)
    {
        if (_tenant.IsSuperAdmin)
            return Ok(Array.Empty<object>());

        if (!_tenant.TenantId.HasValue || !await HasActionAsync(ReceiptRozRoute, "VIEW", ct))
            return Forbid();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = dateFrom ?? new DateOnly(today.Year, today.Month, 1);
        var to = dateTo ?? today;
        if (to < from)
            return BadRequest(new { message = "dateTo must be on or after dateFrom." });

        var rows = await _roznamcha.ListReceiptRozAsync(_tenant.RequiredTenantId, from, to, ct);
        return Ok(rows);
    }

    [HttpGet("receipt-roz/lookups")]
    public async Task<IActionResult> ReceiptRozLookups(CancellationToken ct)
    {
        if (_tenant.IsSuperAdmin) return Ok(new { categories = Array.Empty<object>(), accounts = Array.Empty<object>() });
        if (!_tenant.TenantId.HasValue || !await HasActionAsync(ReceiptRozRoute, "VIEW", ct)) return Forbid();

        return Ok(await LoadRoznamchaLookupsAsync(ct));
    }

    private async Task<object> LoadRoznamchaLookupsAsync(CancellationToken ct)
    {

        var categories = await _db.AccountsCategories.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name)
            .Select(x => new { x.Id, x.Name, x.Code }).ToListAsync(ct);
        var accounts = await _db.AccountsChartAccounts.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.AccountName)
            .Select(x => new { x.Id, x.CategoryId, x.ParentId, x.AccountName, x.AccountNumber, x.AccountCode }).ToListAsync(ct);
        var transactionTypes = await _db.AccountsTransTypes.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name)
            .Select(x => new { x.Id, x.Code, x.Name }).ToListAsync(ct);
        var projects = await _db.AccountsProjects.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name)
            .Select(x => new { x.Id, x.Name }).ToListAsync(ct);
        var currencies = await _db.AccountsCurrencies.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name)
            .Select(x => new { x.Id, x.Code, x.Name }).ToListAsync(ct);
        var transactionModes = await _db.AccountsTransModes.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name)
            .Select(x => new { x.Id, x.Code, x.Name }).ToListAsync(ct);
        var taxTypes = await _db.AccountsTaxTypes.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name)
            .Select(x => new { x.Id, x.Code, x.Name, x.DefaultRate }).ToListAsync(ct);
        var statuses = await _db.AccountsEntryStatuses.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name)
            .Select(x => new { x.Id, x.Code, x.Name, x.ColorCode, x.FontColor }).ToListAsync(ct);

        return new { categories, accounts, transactionTypes, projects, currencies, transactionModes, taxTypes, statuses };
    }

    [HttpPost("payment-roz")]
    [Idempotent]
    public async Task<IActionResult> CreatePaymentRoz([FromBody] CreateRoznamchaPaymentRequest request, CancellationToken ct)
    {
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue || !await HasActionAsync(PaymentRozRoute, "ADD", ct))
            return Forbid();
        try
        {
            request.TransactionTypeId ??= await _db.AccountsTransTypes.AsNoTracking()
                .Where(x => x.IsActive && x.Code == "PAYMENT")
                .OrderByDescending(x => x.TenantId.HasValue)
                .Select(x => (int?)x.Id)
                .FirstOrDefaultAsync(ct);
            request.EntryStatusId = await _db.AccountsEntryStatuses.AsNoTracking()
                .Where(x => x.IsActive && (x.Code == "ENTERED" || x.Code == "DRAFT"))
                .OrderByDescending(x => x.Code == "ENTERED")
                .ThenByDescending(x => x.TenantId.HasValue)
                .Select(x => (int?)x.Id)
                .FirstOrDefaultAsync(ct);
            var row = await _transactions.CreateAsync(1, request, ct);
            return Ok(ApiResponse<RoznamchaEntryDto>.Ok(row, "Payment added successfully."));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object?>.Fail(ex.Message));
        }
    }

    [HttpPost("payment-roz/{id:long}/attachments")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> AddPaymentAttachment(long id, [FromForm] UploadAccountsFileRequest request, CancellationToken ct)
    {
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue || !await HasActionAsync(PaymentRozRoute, "ADD", ct))
            return Forbid();
        try
        {
            var row = await _transactions.AddAttachmentAsync(id, request.File, request.Remarks, ct);
            return Ok(ApiResponse<EntryAttachmentDto>.Ok(row, "Attachment uploaded successfully."));
        }
        catch (KeyNotFoundException ex) { return NotFound(ApiResponse<object?>.Fail(ex.Message)); }
        catch (InvalidOperationException ex) { return BadRequest(ApiResponse<object?>.Fail(ex.Message)); }
    }

    [HttpPost("receipt-roz")]
    [Idempotent]
    public async Task<IActionResult> CreateReceiptRoz([FromBody] CreateRoznamchaReceiptRequest request, CancellationToken ct)
    {
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue || !await HasActionAsync(ReceiptRozRoute, "ADD", ct))
            return Forbid();
        try
        {
            request.TransactionTypeId ??= await _db.AccountsTransTypes.AsNoTracking()
                .Where(x => x.IsActive && x.Code == "RECEIPT")
                .OrderByDescending(x => x.TenantId.HasValue)
                .Select(x => (int?)x.Id)
                .FirstOrDefaultAsync(ct);
            request.EntryStatusId = await _db.AccountsEntryStatuses.AsNoTracking()
                .Where(x => x.IsActive && (x.Code == "ENTERED" || x.Code == "DRAFT"))
                .OrderByDescending(x => x.Code == "ENTERED")
                .ThenByDescending(x => x.TenantId.HasValue)
                .Select(x => (int?)x.Id)
                .FirstOrDefaultAsync(ct);
            var row = await _transactions.CreateAsync(2, request, ct);
            return Ok(ApiResponse<RoznamchaEntryDto>.Ok(row, "Receipt added successfully."));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object?>.Fail(ex.Message));
        }
    }

    [HttpPost("receipt-roz/{id:long}/attachments")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> AddReceiptAttachment(long id, [FromForm] UploadAccountsFileRequest request, CancellationToken ct)
    {
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue || !await HasActionAsync(ReceiptRozRoute, "ADD", ct))
            return Forbid();
        try
        {
            var row = await _transactions.AddAttachmentAsync(id, request.File, request.Remarks, ct);
            return Ok(ApiResponse<EntryAttachmentDto>.Ok(row, "Attachment uploaded successfully."));
        }
        catch (KeyNotFoundException ex) { return NotFound(ApiResponse<object?>.Fail(ex.Message)); }
        catch (InvalidOperationException ex) { return BadRequest(ApiResponse<object?>.Fail(ex.Message)); }
    }

    [HttpPost("receipt-roz/process-selected")]
    [Idempotent]
    public async Task<IActionResult> ProcessSelectedReceipts([FromBody] ProcessReceiptRozRequest request, CancellationToken ct)
    {
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue || !await HasActionAsync(ReceiptRozRoute, "EDIT", ct))
            return Forbid();

        var ids = request.Ids.Where(id => id > 0).Distinct().ToArray();
        if (ids.Length == 0)
            return BadRequest(ApiResponse<object?>.Fail("Select at least one receipt row first."));

        var receiptTypeId = await ResolveRoznamchaTypeIdAsync("RECEIPT", ct);
        var approvedStatusId = await ResolveEntryStatusIdAsync("APPROVED", ct);
        if (!receiptTypeId.HasValue || !approvedStatusId.HasValue)
            return BadRequest(ApiResponse<object?>.Fail("Receipt or Approved status configuration is missing."));

        var validIds = await _db.RoznamchaEntries.AsNoTracking()
            .Where(x => ids.Contains(x.Id) && !x.IsDeleted && x.RoznamchaTypeId == receiptTypeId.Value)
            .Select(x => x.Id)
            .ToListAsync(ct);
        if (validIds.Count != ids.Length)
            return BadRequest(ApiResponse<object?>.Fail("One or more selected receipts were not found."));

        foreach (var id in validIds)
        {
            await _transactions.UpdateStatusAsync(id, new UpdateRoznamchaStatusRequest
            {
                EntryStatusId = approvedStatusId,
                Remarks = request.Comments
            }, ct);
            await _transactions.ApproveAsync(id, ct);
        }

        return Ok(ApiResponse<object>.Ok(new { processedCount = validIds.Count }, $"{validIds.Count} receipt(s) processed successfully."));
    }

    [HttpPost("receipt-roz/settle")]
    [Idempotent]
    public async Task<IActionResult> SettleReceipts([FromBody] SettleReceiptRozRequest request, CancellationToken ct)
    {
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue || !await HasActionAsync(ReceiptRozRoute, "EDIT", ct))
            return Forbid();
        if (request.DateTo < request.DateFrom)
            return BadRequest(ApiResponse<object?>.Fail("Date To must be on or after Date From."));

        var receiptTypeId = await ResolveRoznamchaTypeIdAsync("RECEIPT", ct);
        var settledStatusId = await ResolveEntryStatusIdAsync("SETTLED", ct);
        if (!receiptTypeId.HasValue || !settledStatusId.HasValue)
            return BadRequest(ApiResponse<object?>.Fail("Receipt or Settled status configuration is missing."));

        var ids = await _db.RoznamchaEntries.AsNoTracking()
            .Where(x => !x.IsDeleted && !x.IsSettled && x.RoznamchaTypeId == receiptTypeId.Value
                && x.TransDate >= request.DateFrom && x.TransDate <= request.DateTo)
            .Select(x => x.Id)
            .ToListAsync(ct);

        foreach (var id in ids)
        {
            await _transactions.UpdateStatusAsync(id, new UpdateRoznamchaStatusRequest
            {
                EntryStatusId = settledStatusId,
                IsSettled = true,
                Remarks = request.Comments
            }, ct);
        }

        return Ok(ApiResponse<object>.Ok(new { settledCount = ids.Count }, $"{ids.Count} receipt(s) settled successfully."));
    }

    private Task<int?> ResolveRoznamchaTypeIdAsync(string code, CancellationToken ct) =>
        _db.AccountsRoznamchaTypes.AsNoTracking().Where(x => x.IsActive && x.Code == code)
            .OrderByDescending(x => x.TenantId.HasValue).Select(x => (int?)x.Id).FirstOrDefaultAsync(ct);

    private Task<int?> ResolveEntryStatusIdAsync(string code, CancellationToken ct) =>
        _db.AccountsEntryStatuses.AsNoTracking().Where(x => x.IsActive && x.Code == code)
            .OrderByDescending(x => x.TenantId.HasValue).Select(x => (int?)x.Id).FirstOrDefaultAsync(ct);

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

public sealed class ProcessReceiptRozRequest
{
    public List<long> Ids { get; set; } = [];
    public string? Comments { get; set; }
}

public sealed class SettleReceiptRozRequest
{
    public DateOnly DateFrom { get; set; }
    public DateOnly DateTo { get; set; }
    public string? Comments { get; set; }
}
