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
    private const string RoznamchaUpdateRoute = "/accounts/roznamcha-update";

    private readonly ApplicationDbContext _db;
    private readonly ITenantService _tenant;
    private readonly TenantPermissionService _tenantPermissions;
    private readonly RbacService _rbac;
    private readonly IAccountsRoznamchaService _roznamcha;
    private readonly IRoznamchaService _transactions;
    private readonly ILibraryService _library;

    public AccountsRoznamchaController(
        ApplicationDbContext db,
        ITenantService tenant,
        TenantPermissionService tenantPermissions,
        RbacService rbac,
        IAccountsRoznamchaService roznamcha,
        IRoznamchaService transactions,
        ILibraryService library)
    {
        _db = db;
        _tenant = tenant;
        _tenantPermissions = tenantPermissions;
        _rbac = rbac;
        _roznamcha = roznamcha;
        _transactions = transactions;
        _library = library;
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

    /// <summary>Unified ROZ grid (legacy Sp_vgetRoznamcha parity). Optional rozTypeId filters payment/receipt type.</summary>
    [HttpGet("roznamcha-update")]
    public async Task<IActionResult> ListRoznamchaUpdate(
        [FromQuery] DateOnly? dateFrom,
        [FromQuery] DateOnly? dateTo,
        [FromQuery] int? rozTypeId,
        [FromQuery] string? typeCode,
        CancellationToken ct)
    {
        if (_tenant.IsSuperAdmin)
            return Ok(Array.Empty<object>());

        if (!_tenant.TenantId.HasValue || !await HasActionAsync(RoznamchaUpdateRoute, "VIEW", ct))
            return Forbid();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = dateFrom ?? new DateOnly(today.Year, today.Month, 1);
        var to = dateTo ?? today;
        if (to < from)
            return BadRequest(new { message = "dateTo must be on or after dateFrom." });

        var tenantId = _tenant.RequiredTenantId;
        if (!rozTypeId.HasValue && !string.IsNullOrWhiteSpace(typeCode))
            rozTypeId = await _roznamcha.ResolveRozTypeIdAsync(tenantId, typeCode.Trim(), ct);

        var rows = await _roznamcha.ListRoznamchaAsync(tenantId, from, to, rozTypeId, ct);
        return Ok(rows);
    }

    [HttpGet("show-record")]
    public async Task<IActionResult> ListHiddenRecords(
        [FromQuery] DateOnly? dateFrom, [FromQuery] DateOnly? dateTo, CancellationToken ct)
    {
        if (_tenant.IsSuperAdmin) return Ok(Array.Empty<AccountsRecordRowDto>());
        if (!_tenant.TenantId.HasValue || !await HasActionAsync("/accounts/show-record", "VIEW", ct)) return Forbid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var startDate = dateFrom ?? new DateOnly(today.Year, today.Month, 1);
        var endDate = dateTo ?? today;
        if (endDate < startDate) return BadRequest(new { message = "dateTo must be on or after dateFrom." });

        var rows = await (
            from entry in _db.RoznamchaEntries.AsNoTracking()
            join type in _db.AccountsRoznamchaTypes.AsNoTracking() on entry.RoznamchaTypeId equals type.Id into typeRows
            from type in typeRows.DefaultIfEmpty()
            join category in _db.AccountsCategories.AsNoTracking() on entry.CategoryId equals category.Id into categoryRows
            from category in categoryRows.DefaultIfEmpty()
            join fromAccount in _db.AccountsChartAccounts.AsNoTracking() on entry.FromAccountId equals fromAccount.Id into fromRows
            from fromAccount in fromRows.DefaultIfEmpty()
            join toAccount in _db.AccountsChartAccounts.AsNoTracking() on entry.ToAccountId equals toAccount.Id into toRows
            from toAccount in toRows.DefaultIfEmpty()
            join status in _db.AccountsEntryStatuses.AsNoTracking() on entry.EnterStatusId equals status.Id into statusRows
            from status in statusRows.DefaultIfEmpty()
            where (entry.IsDeleted || !entry.IsShow) && entry.TransDate >= startDate && entry.TransDate <= endDate
            orderby entry.TransDate descending, entry.Id descending
            select new AccountsRecordRowDto
            {
                Id = entry.Id, SerialNo = entry.SNo, ReferenceNo = entry.Ref,
                Type = type == null ? null : type.Name, TransactionDate = entry.TransDate,
                CreatedOnUtc = entry.CreatedOnUtc, Category = category == null ? null : category.Name,
                FromAccount = fromAccount == null ? null : fromAccount.AccountName,
                ToAccount = toAccount == null ? null : toAccount.AccountName,
                Amount = (entry.Amount ?? 0) + (entry.Adjustment ?? 0), UsdAmount = entry.UsdAmount ?? 0,
                Status = status == null ? null : status.Name, Description = entry.Descriptions,
                Remarks = entry.Remarks, IsHidden = !entry.IsShow, IsDeleted = entry.IsDeleted
            }).ToListAsync(ct);
        return Ok(rows);
    }

    [HttpPost("show-record/{id:long}/restore")]
    [Idempotent]
    public async Task<IActionResult> RestoreRecord(long id, CancellationToken ct)
    {
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue || !await HasActionAsync("/accounts/show-record", "EDIT", ct))
            return Forbid();
        var row = await _db.RoznamchaEntries.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row == null) return NotFound(ApiResponse<object?>.Fail("Record was not found."));
        row.IsDeleted = false;
        row.IsShow = true;
        row.UpdatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        row.UpdatedOnUtc = DateTime.UtcNow;
        _db.RoznamchaEntryProcessLogs.Add(new Accounts.Models.RoznamchaEntryProcessLog
        {
            TenantId = _tenant.RequiredTenantId, RoznamchaEntryId = row.Id,
            Action = "Restored", ProcessedByUserId = row.UpdatedByUserId
        });
        await _db.SaveChangesAsync(ct);
        return Ok(ApiResponse<object>.Ok(new { id }, "Record restored successfully."));
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
        var libraryDocuments = await _db.LibraryDocuments.AsNoTracking()
            .Where(x => x.IsActive && x.AssetKind == "Document")
            .OrderByDescending(x => x.CreatedOnUtc).Take(100)
            .Select(x => new
            {
                x.Id,
                DocumentReference = x.DocumentReference ?? ("Doc-" + x.Id),
                x.Title,
                x.LibraryTypeId,
                LibraryTypeName = x.LibraryType == null ? null : x.LibraryType.Name,
                x.LibrarySubTypeId
            }).ToListAsync(ct);
        var products = await _db.SaleRoznamchaProducts.AsNoTracking()
            .Where(x => x.IsActive && !x.IsDeleted)
            .OrderBy(x => x.Name)
            .Select(x => new
            {
                x.Id,
                ProductCode = x.ProductCode,
                Name = x.Name,
                QuantityOnHand = x.QuantityOnHand,
                PurchasePrice = x.PurchasePrice,
                x.CurrencyId
            }).ToListAsync(ct);

        return new { categories, accounts, transactionTypes, projects, currencies, transactionModes, taxTypes, statuses, libraryDocuments, products };
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

    [HttpGet("payment-roz/{id:long}")]
    public Task<IActionResult> GetPaymentRoz(long id, CancellationToken ct) => GetRozEntry(id, "PAYMENT", PaymentRozRoute, ct);

    [HttpPut("payment-roz/{id:long}")]
    public Task<IActionResult> UpdatePaymentRoz(long id, [FromBody] CreateRoznamchaPaymentRequest request, CancellationToken ct) =>
        UpdateRozEntry(id, 1, request, PaymentRozRoute, ct);

    [HttpDelete("payment-roz/{id:long}")]
    public Task<IActionResult> DeletePaymentRoz(long id, [FromBody] DeleteRoznamchaRequest? request, CancellationToken ct) =>
        DeleteRozEntry(id, "PAYMENT", PaymentRozRoute, request?.Remarks, ct);

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

    [HttpPost("payment-roz/process-selected")]
    [Idempotent]
    public async Task<IActionResult> ProcessSelectedPayments([FromBody] ProcessReceiptRozRequest request, CancellationToken ct)
    {
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue || !await HasActionAsync(PaymentRozRoute, "EDIT", ct))
            return Forbid();

        var ids = request.Ids.Where(id => id > 0).Distinct().ToArray();
        if (ids.Length == 0)
            return BadRequest(ApiResponse<object?>.Fail("Select at least one payment row first."));

        var paymentTypeId = await ResolveRoznamchaTypeIdAsync("PAYMENT", ct);
        var approvedStatusId = await ResolveEntryStatusIdAsync("APPROVED", ct);
        if (!paymentTypeId.HasValue || !approvedStatusId.HasValue)
            return BadRequest(ApiResponse<object?>.Fail("Payment or Approved status configuration is missing."));

        var validIds = await _db.RoznamchaEntries.AsNoTracking()
            .Where(x => ids.Contains(x.Id) && !x.IsDeleted && x.RoznamchaTypeId == paymentTypeId.Value)
            .Select(x => x.Id)
            .ToListAsync(ct);
        if (validIds.Count != ids.Length)
            return BadRequest(ApiResponse<object?>.Fail("One or more selected payments were not found."));

        foreach (var id in validIds)
        {
            await _transactions.UpdateStatusAsync(id, new UpdateRoznamchaStatusRequest
            {
                EntryStatusId = approvedStatusId,
                Remarks = request.Comments
            }, ct);
            await _transactions.ApproveAsync(id, ct);
        }

        return Ok(ApiResponse<object>.Ok(new { processedCount = validIds.Count }, $"{validIds.Count} payment(s) processed successfully."));
    }

    [HttpPost("payment-roz/settle")]
    [Idempotent]
    public async Task<IActionResult> SettlePayments([FromBody] SettleReceiptRozRequest request, CancellationToken ct)
    {
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue || !await HasActionAsync(PaymentRozRoute, "EDIT", ct))
            return Forbid();
        if (request.DateTo < request.DateFrom)
            return BadRequest(ApiResponse<object?>.Fail("Date To must be on or after Date From."));

        var paymentTypeId = await ResolveRoznamchaTypeIdAsync("PAYMENT", ct);
        var settledStatusId = await ResolveEntryStatusIdAsync("SETTLED", ct);
        if (!paymentTypeId.HasValue || !settledStatusId.HasValue)
            return BadRequest(ApiResponse<object?>.Fail("Payment or Settled status configuration is missing."));

        var ids = await _db.RoznamchaEntries.AsNoTracking()
            .Where(x => !x.IsDeleted && !x.IsSettled && x.RoznamchaTypeId == paymentTypeId.Value
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

        return Ok(ApiResponse<object>.Ok(new { settledCount = ids.Count }, $"{ids.Count} payment(s) settled successfully."));
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

    [HttpGet("receipt-roz/{id:long}")]
    public Task<IActionResult> GetReceiptRoz(long id, CancellationToken ct) => GetRozEntry(id, "RECEIPT", ReceiptRozRoute, ct);

    [HttpPut("receipt-roz/{id:long}")]
    public Task<IActionResult> UpdateReceiptRoz(long id, [FromBody] CreateRoznamchaReceiptRequest request, CancellationToken ct) =>
        UpdateRozEntry(id, 2, request, ReceiptRozRoute, ct);

    [HttpDelete("receipt-roz/{id:long}")]
    public Task<IActionResult> DeleteReceiptRoz(long id, [FromBody] DeleteRoznamchaRequest? request, CancellationToken ct) =>
        DeleteRozEntry(id, "RECEIPT", ReceiptRozRoute, request?.Remarks, ct);

    [HttpGet("entry/{id:long}/attachments")]
    public async Task<IActionResult> ListEntryAttachments(long id, CancellationToken ct)
    {
        var route = await ResolveEntryRouteAsync(id, ct);
        if (route is null) return NotFound(ApiResponse<object?>.Fail("Transaction was not found."));
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue || !await HasActionAsync(route, "VIEW", ct)) return Forbid();
        try { return Ok(ApiResponse<IReadOnlyList<EntryAttachmentDto>>.Ok(await _transactions.ListAttachmentsAsync(id, ct))); }
        catch (KeyNotFoundException ex) { return NotFound(ApiResponse<object?>.Fail(ex.Message)); }
    }

    [HttpGet("entry/{id:long}/process-log")]
    public async Task<IActionResult> ListEntryProcessLog(long id, CancellationToken ct)
    {
        var route = await ResolveEntryRouteAsync(id, ct);
        if (route is null) return NotFound(ApiResponse<object?>.Fail("Transaction was not found."));
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue || !await HasActionAsync(route, "VIEW", ct)) return Forbid();
        try { return Ok(ApiResponse<IReadOnlyList<RoznamchaProcessLogDto>>.Ok(await _transactions.ListProcessLogsAsync(id, ct))); }
        catch (KeyNotFoundException ex) { return NotFound(ApiResponse<object?>.Fail(ex.Message)); }
    }

    [HttpGet("entry/{id:long}/library-documents")]
    public async Task<IActionResult> ListLibraryDocuments(long id, CancellationToken ct)
    {
        var route = await ResolveEntryRouteAsync(id, ct);
        if (route is null) return NotFound(ApiResponse<object?>.Fail("Transaction was not found."));
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue || !await HasActionAsync(route, "VIEW", ct)) return Forbid();
        try { return Ok(ApiResponse<IReadOnlyList<LibraryDocumentLinkDto>>.Ok(await _transactions.ListLibraryDocumentsAsync(id, ct))); }
        catch (KeyNotFoundException ex) { return NotFound(ApiResponse<object?>.Fail(ex.Message)); }
    }

    [HttpGet("library-documents/picker")]
    public async Task<IActionResult> SearchLibraryDocumentsForEntry(
        [FromQuery] string kind, [FromQuery] string? search, [FromQuery] int? typeId,
        [FromQuery] int? subTypeId, [FromQuery] int take = 30, CancellationToken ct = default)
    {
        var route = kind.Equals("receipt", StringComparison.OrdinalIgnoreCase)
            ? ReceiptRozRoute
            : kind.Equals("payment", StringComparison.OrdinalIgnoreCase)
                ? PaymentRozRoute
                : null;
        if (route == null) return BadRequest(ApiResponse<object?>.Fail("kind must be payment or receipt."));
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue || !await HasActionAsync(route, "VIEW", ct)) return Forbid();
        return Ok(ApiResponse<IReadOnlyList<LibraryPickerItemDto>>.Ok(
            await _library.SearchDocumentsAsync(search, typeId, subTypeId, take, ct)));
    }

    [HttpGet("entry/{id:long}/library-documents/picker")]
    public async Task<IActionResult> SearchLibraryDocuments(
        long id, [FromQuery] string? search, [FromQuery] int? typeId,
        [FromQuery] int? subTypeId, [FromQuery] int take = 30, CancellationToken ct = default)
    {
        var route = await ResolveEntryRouteAsync(id, ct);
        if (route is null) return NotFound(ApiResponse<object?>.Fail("Transaction was not found."));
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue || !await HasActionAsync(route, "VIEW", ct)) return Forbid();
        return Ok(ApiResponse<IReadOnlyList<LibraryPickerItemDto>>.Ok(
            await _library.SearchDocumentsAsync(search, typeId, subTypeId, take, ct)));
    }

    [HttpPost("entry/{id:long}/library-documents")]
    public async Task<IActionResult> LinkLibraryDocuments(long id, [FromBody] LinkLibraryDocumentsRequest request, CancellationToken ct)
    {
        var route = await ResolveEntryRouteAsync(id, ct);
        if (route is null) return NotFound(ApiResponse<object?>.Fail("Transaction was not found."));
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue || !await HasActionAsync(route, "EDIT", ct)) return Forbid();
        try
        {
            var rows = await _transactions.LinkLibraryDocumentsAsync(id, request.LibraryDocumentIds, ct);
            return Ok(ApiResponse<IReadOnlyList<LibraryDocumentLinkDto>>.Ok(rows, "Library document(s) linked successfully."));
        }
        catch (KeyNotFoundException ex) { return NotFound(ApiResponse<object?>.Fail(ex.Message)); }
        catch (InvalidOperationException ex) { return BadRequest(ApiResponse<object?>.Fail(ex.Message)); }
    }

    [HttpDelete("entry/{id:long}/library-documents/{documentId:long}")]
    public async Task<IActionResult> UnlinkLibraryDocument(long id, long documentId, CancellationToken ct)
    {
        var route = await ResolveEntryRouteAsync(id, ct);
        if (route is null) return NotFound(ApiResponse<object?>.Fail("Transaction was not found."));
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue || !await HasActionAsync(route, "EDIT", ct)) return Forbid();
        try
        {
            await _transactions.UnlinkLibraryDocumentAsync(id, documentId, ct);
            return Ok(ApiResponse<object?>.Ok(null, "Library document unlinked successfully."));
        }
        catch (KeyNotFoundException ex) { return NotFound(ApiResponse<object?>.Fail(ex.Message)); }
    }

    [HttpGet("entry/{id:long}/library-documents/{documentId:long}/download")]
    public async Task<IActionResult> DownloadLibraryDocument(long id, long documentId, CancellationToken ct)
    {
        var route = await ResolveEntryRouteAsync(id, ct);
        if (route is null) return NotFound(ApiResponse<object?>.Fail("Transaction was not found."));
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue || !await HasActionAsync(route, "VIEW", ct)) return Forbid();
        var linked = await _db.RoznamchaLibraryDocuments.AsNoTracking()
            .AnyAsync(x => x.RoznamchaEntryId == id && x.LibraryDocumentId == documentId, ct);
        if (!linked) return NotFound(ApiResponse<object?>.Fail("Linked library document was not found."));
        var file = await _library.GetDocumentFileAsync(documentId, activeOnly: true, ct);
        if (file == null || !System.IO.File.Exists(file.Value.Path))
            return NotFound(ApiResponse<object?>.Fail("Library document file was not found."));
        return PhysicalFile(file.Value.Path, file.Value.ContentType, file.Value.FileName, enableRangeProcessing: true);
    }

    [HttpGet("attachments/{attachmentId:long}/download")]
    public async Task<IActionResult> DownloadEntryAttachment(long attachmentId, CancellationToken ct)
    {
        var entryId = await _db.AccountsEntryDocuments.AsNoTracking()
            .Where(x => x.Id == attachmentId && x.RoznamchaEntryId != null)
            .Select(x => x.RoznamchaEntryId).FirstOrDefaultAsync(ct);
        if (!entryId.HasValue) return NotFound(ApiResponse<object?>.Fail("Attachment was not found."));
        var route = await ResolveEntryRouteAsync(entryId.Value, ct);
        if (route is null || _tenant.IsSuperAdmin || !_tenant.TenantId.HasValue || !await HasActionAsync(route, "VIEW", ct)) return Forbid();
        var file = await _transactions.OpenAttachmentAsync(attachmentId, ct);
        return file is null ? NotFound(ApiResponse<object?>.Fail("Attachment file was not found.")) : File(file.Value.Stream, file.Value.ContentType, file.Value.FileName);
    }

    [HttpDelete("attachments/{attachmentId:long}")]
    public async Task<IActionResult> DeleteEntryAttachment(long attachmentId, CancellationToken ct)
    {
        var entryId = await _db.AccountsEntryDocuments.AsNoTracking()
            .Where(x => x.Id == attachmentId && x.RoznamchaEntryId != null)
            .Select(x => x.RoznamchaEntryId).FirstOrDefaultAsync(ct);
        if (!entryId.HasValue) return NotFound(ApiResponse<object?>.Fail("Attachment was not found."));
        var route = await ResolveEntryRouteAsync(entryId.Value, ct);
        if (route is null || _tenant.IsSuperAdmin || !_tenant.TenantId.HasValue || !await HasActionAsync(route, "EDIT", ct)) return Forbid();
        await _transactions.DeleteAttachmentAsync(attachmentId, ct);
        return Ok(ApiResponse<object?>.Ok(null, "Attachment deleted successfully."));
    }

    [HttpPost("roznamcha-update/status-selected")]
    [Idempotent]
    public async Task<IActionResult> UpdateSelectedStatus([FromBody] BulkRoznamchaStatusRequest request, CancellationToken ct)
    {
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue || !await HasActionAsync(RoznamchaUpdateRoute, "EDIT", ct)) return Forbid();
        var ids = request.Ids.Where(x => x > 0).Distinct().ToArray();
        if (ids.Length == 0) return BadRequest(ApiResponse<object?>.Fail("Select at least one entry."));
        var status = await _db.AccountsEntryStatuses.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.EntryStatusId && x.IsActive, ct);
        if (status is null) return BadRequest(ApiResponse<object?>.Fail("Selected status was not found."));
        var entries = await _db.RoznamchaEntries.Where(x => ids.Contains(x.Id) && !x.IsDeleted).ToListAsync(ct);
        if (entries.Count != ids.Length) return BadRequest(ApiResponse<object?>.Fail("One or more entries were not found."));
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var approved = status.Code.Equals("APPROVED", StringComparison.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (entry.IsLocked && entry.EnterStatusId != request.EntryStatusId)
                return BadRequest(ApiResponse<object?>.Fail($"Locked entry {entry.Ref ?? entry.Id.ToString()} cannot be changed."));
            entry.EnterStatusId = request.EntryStatusId;
            entry.IsApproved = approved || entry.IsApproved;
            entry.IsLocked = (approved && request.LockWhenApproved) || entry.IsLocked;
            entry.UpdatedByUserId = userId;
            entry.UpdatedOnUtc = DateTime.UtcNow;
            _db.RoznamchaEntryProcessLogs.Add(new Accounts.Models.RoznamchaEntryProcessLog
            {
                TenantId = _tenant.RequiredTenantId, RoznamchaEntryId = entry.Id,
                Action = $"Status:{status.Code}", Notes = request.Comments,
                ProcessedByUserId = userId
            });
        }
        await _db.SaveChangesAsync(ct);
        return Ok(ApiResponse<object>.Ok(new { updatedCount = entries.Count }, $"{entries.Count} entry(s) updated successfully."));
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

    private async Task<IActionResult> GetRozEntry(long id, string typeCode, string route, CancellationToken ct)
    {
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue || !await HasActionAsync(route, "VIEW", ct))
            return Forbid();
        var typeId = await ResolveRoznamchaTypeIdAsync(typeCode, ct);
        var belongsToType = typeId.HasValue && await _db.RoznamchaEntries.AsNoTracking()
            .AnyAsync(x => x.Id == id && x.RoznamchaTypeId == typeId.Value && !x.IsDeleted, ct);
        var row = belongsToType ? await _transactions.GetAsync(id, ct) : null;
        return row is null
            ? NotFound(ApiResponse<object?>.Fail("Transaction was not found."))
            : Ok(ApiResponse<RoznamchaEntryDto>.Ok(row));
    }

    private async Task<IActionResult> UpdateRozEntry(long id, int entryType, SaveRoznamchaEntryRequest request, string route, CancellationToken ct)
    {
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue || !await HasActionAsync(route, "EDIT", ct))
            return Forbid();
        try
        {
            var typeCode = entryType == 1 ? "PAYMENT" : "RECEIPT";
            var typeId = await ResolveRoznamchaTypeIdAsync(typeCode, ct);
            if (!typeId.HasValue || !await _db.RoznamchaEntries.AsNoTracking()
                    .AnyAsync(x => x.Id == id && x.RoznamchaTypeId == typeId.Value && !x.IsDeleted, ct))
                return NotFound(ApiResponse<object?>.Fail("Transaction was not found."));
            var row = await _transactions.UpdateAsync(id, entryType, request, ct);
            return Ok(ApiResponse<RoznamchaEntryDto>.Ok(row, "Transaction updated successfully."));
        }
        catch (KeyNotFoundException ex) { return NotFound(ApiResponse<object?>.Fail(ex.Message)); }
        catch (InvalidOperationException ex) { return BadRequest(ApiResponse<object?>.Fail(ex.Message)); }
    }

    private async Task<IActionResult> DeleteRozEntry(long id, string typeCode, string route, string? remarks, CancellationToken ct)
    {
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue || !await HasActionAsync(route, "DELETE", ct))
            return Forbid();
        try
        {
            var typeId = await ResolveRoznamchaTypeIdAsync(typeCode, ct);
            if (!typeId.HasValue || !await _db.RoznamchaEntries.AsNoTracking()
                    .AnyAsync(x => x.Id == id && x.RoznamchaTypeId == typeId.Value && !x.IsDeleted, ct))
                return NotFound(ApiResponse<object?>.Fail("Transaction was not found."));
            await _transactions.SoftDeleteAsync(id, remarks, ct);
            return Ok(ApiResponse<object?>.Ok(null, "Transaction deleted successfully."));
        }
        catch (KeyNotFoundException ex) { return NotFound(ApiResponse<object?>.Fail(ex.Message)); }
        catch (InvalidOperationException ex) { return BadRequest(ApiResponse<object?>.Fail(ex.Message)); }
    }

    private async Task<string?> ResolveEntryRouteAsync(long id, CancellationToken ct)
    {
        var code = await (from entry in _db.RoznamchaEntries.AsNoTracking()
                          join type in _db.AccountsRoznamchaTypes.AsNoTracking() on entry.RoznamchaTypeId equals type.Id
                          where entry.Id == id
                          select type.Code).FirstOrDefaultAsync(ct);
        return code?.ToUpperInvariant() switch { "PAYMENT" => PaymentRozRoute, "RECEIPT" => ReceiptRozRoute, _ => RoznamchaUpdateRoute };
    }

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

public sealed class LinkLibraryDocumentsRequest
{
    public List<long> LibraryDocumentIds { get; set; } = [];
}
