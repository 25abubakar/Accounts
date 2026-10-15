using Accounts.Data;
using Accounts.DTOs;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Services;

public sealed class RoznamchaService(
    ApplicationDbContext db,
    ICurrentUserService current,
    IReferenceGeneratorService references,
    IFileStorageService files) : IRoznamchaService
{
    public async Task<IReadOnlyList<RoznamchaEntryDto>> ListAsync(int entryTypeId, DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        EnsureEntryType(entryTypeId);
        var query = EntryQuery().Where(x => x.EntryTypeId == entryTypeId && !x.IsDeleted);
        if (from.HasValue) query = query.Where(x => x.TransactionDate >= from.Value);
        if (to.HasValue) query = query.Where(x => x.TransactionDate <= to.Value);
        return await query.OrderByDescending(x => x.TransactionDate).ThenByDescending(x => x.Id).ToListAsync(ct);
    }

    public Task<RoznamchaEntryDto?> GetAsync(long id, CancellationToken ct = default) =>
        EntryQuery().FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<RoznamchaEntryDto> CreateAsync(int entryTypeId, SaveRoznamchaEntryRequest request, CancellationToken ct = default)
    {
        EnsureEntryType(entryTypeId);
        await ValidateAsync(entryTypeId, request, ct);
        await ValidateLibraryDocumentsAsync(request.LibraryDocumentIds ?? [], ct);
        var gross = decimal.Round(request.Amount, 2, MidpointRounding.AwayFromZero);
        var taxRate = request.TaxRate.GetValueOrDefault();
        if (taxRate < 0 || taxRate > 100) throw new InvalidOperationException("Tax rate must be between 0 and 100.");
        var tax = request.TaxTypeId.HasValue || taxRate > 0
            ? decimal.Round(gross * taxRate / 100m, 2, MidpointRounding.AwayFromZero)
            : 0m;
        var net = gross - tax;
        if (net < 0) throw new InvalidOperationException("Tax cannot exceed the transaction amount.");
        var isUsd = await IsUsdCurrencyAsync(request.CurrencyId, ct);

        var row = new RoznamchaEntry
        {
            TenantId = current.TenantId,
            SNo = await db.RoznamchaEntries.CountAsync(ct) + 1,
            Ref = entryTypeId == 1
                ? await references.PaymentReferenceAsync(current.TenantId, ct)
                : await references.ReceiptReferenceAsync(current.TenantId, ct),
            RoznamchaTypeId = await ResolveRoznamchaTypeIdAsync(entryTypeId, ct),
            TransTypeId = request.TransactionTypeId ?? entryTypeId,
            CreatedByUserId = current.UserId,
            CreatedOnUtc = DateTime.UtcNow
        };
        Apply(row, request, net, tax, isUsd);
        await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
        db.RoznamchaEntries.Add(row);
        await db.SaveChangesAsync(ct);
        await ReconcileLibraryDocumentsAsync(row, request.LibraryDocumentIds ?? [], ct);
        await db.SaveChangesAsync(ct);
        await ApplyInventoryPurchaseFromPaymentAsync(entryTypeId, row, request, isUsd, ct);
        if (transaction != null) await transaction.CommitAsync(ct);
        await AddProcessLogAsync(row.Id, "Created", null, ct);
        return await GetAsync(row.Id, ct) ?? throw new InvalidOperationException("Saved transaction could not be reloaded.");
    }

    public async Task<RoznamchaEntryDto> UpdateAsync(long id, int entryTypeId, SaveRoznamchaEntryRequest request, CancellationToken ct = default)
    {
        EnsureEntryType(entryTypeId);
        var row = await db.RoznamchaEntries.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct)
            ?? throw new KeyNotFoundException("Transaction was not found.");
        if (row.IsLocked) throw new InvalidOperationException("Locked transaction cannot be edited.");
        await EnsureNotInventoryPostedAsync(id, "edited", ct);
        await ValidateAsync(entryTypeId, request, ct);
        if (request.LibraryDocumentIds != null)
            await ValidateLibraryDocumentsAsync(request.LibraryDocumentIds, ct);
        var gross = decimal.Round(request.Amount, 2, MidpointRounding.AwayFromZero);
        var taxRate = request.TaxRate.GetValueOrDefault();
        if (taxRate < 0 || taxRate > 100) throw new InvalidOperationException("Tax rate must be between 0 and 100.");
        var tax = request.TaxTypeId.HasValue || taxRate > 0 ? decimal.Round(gross * taxRate / 100m, 2) : 0m;
        var net = gross - tax;
        if (net < 0) throw new InvalidOperationException("Tax cannot exceed the transaction amount.");
        var isUsd = await IsUsdCurrencyAsync(request.CurrencyId, ct);
        row.RoznamchaTypeId = await ResolveRoznamchaTypeIdAsync(entryTypeId, ct);
        row.TransTypeId = request.TransactionTypeId ?? entryTypeId;
        row.UpdatedByUserId = current.UserId;
        row.UpdatedOnUtc = DateTime.UtcNow;
        Apply(row, request, net, tax, isUsd);
        await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
        if (request.LibraryDocumentIds != null)
            await ReconcileLibraryDocumentsAsync(row, request.LibraryDocumentIds, ct);
        else
            row.IsFromLibrary = !string.IsNullOrWhiteSpace(row.LibRef) ||
                await db.RoznamchaLibraryDocuments.AnyAsync(x => x.RoznamchaEntryId == row.Id, ct);
        await db.SaveChangesAsync(ct);
        if (transaction != null) await transaction.CommitAsync(ct);
        await AddProcessLogAsync(row.Id, "Updated", null, ct);
        return await GetAsync(row.Id, ct) ?? throw new InvalidOperationException("Updated transaction could not be reloaded.");
    }

    public async Task SoftDeleteAsync(long id, string? remarks = null, CancellationToken ct = default)
    {
        var row = await db.RoznamchaEntries.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new KeyNotFoundException("Transaction was not found.");
        if (row.IsLocked) throw new InvalidOperationException("Locked transaction cannot be deleted.");
        await EnsureNotInventoryPostedAsync(id, "deleted", ct);
        row.IsDeleted = true;
        row.UpdatedByUserId = current.UserId;
        row.UpdatedOnUtc = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(remarks)) row.Remarks = Clean(remarks, 2000);
        await db.SaveChangesAsync(ct);
        await AddProcessLogAsync(row.Id, "SoftDeleted", remarks, ct);
    }

    public async Task<RoznamchaEntryDto> UpdateStatusAsync(long id, UpdateRoznamchaStatusRequest request, CancellationToken ct = default)
    {
        var row = await db.RoznamchaEntries.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct)
            ?? throw new KeyNotFoundException("Transaction was not found.");
        if (request.EntryStatusId.HasValue) row.EnterStatusId = request.EntryStatusId;
        if (request.IsSettled.HasValue) row.IsSettled = request.IsSettled.Value;
        if (request.IsShownHidden.HasValue) row.IsShow = !request.IsShownHidden.Value;
        if (request.IsLocked == false)
            await EnsureNotInventoryPostedAsync(id, "unlocked", ct);
        if (request.IsLocked.HasValue) row.IsLocked = request.IsLocked.Value;
        if (!string.IsNullOrWhiteSpace(request.Remarks)) row.Remarks = Clean(request.Remarks, 2000);
        row.UpdatedByUserId = current.UserId;
        row.UpdatedOnUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await AddProcessLogAsync(row.Id, "StatusUpdated", request.Remarks, ct);
        return await GetAsync(row.Id, ct) ?? throw new InvalidOperationException("Updated transaction could not be reloaded.");
    }

    public async Task<RoznamchaEntryDto> ApproveAsync(long id, CancellationToken ct = default)
    {
        var row = await db.RoznamchaEntries.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct)
            ?? throw new KeyNotFoundException("Transaction was not found.");
        row.IsApproved = true;
        row.IsLocked = true;
        row.UpdatedByUserId = current.UserId;
        row.UpdatedOnUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await AddProcessLogAsync(row.Id, "Approved", null, ct);
        return await GetAsync(row.Id, ct) ?? throw new InvalidOperationException("Approved transaction could not be reloaded.");
    }

    public async Task<EntryAttachmentDto> AddAttachmentAsync(long id, IFormFile file, string? remarks, CancellationToken ct = default)
    {
        var entry = await db.RoznamchaEntries.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct)
            ?? throw new KeyNotFoundException("Transaction was not found.");
        var stored = await files.SaveAccountsFileAsync(current.TenantId, file, ct);
        var row = new AccountsEntryDocument
        {
            TenantId = current.TenantId,
            RoznamchaEntryId = entry.Id,
            FileName = stored.FileName,
            StoredPath = stored.StoredPath,
            ContentType = stored.ContentType,
            FileSizeBytes = stored.FileSizeBytes,
            Remarks = Clean(remarks, 2000),
            DocumentReference = await references.DocumentReferenceAsync(current.TenantId, ct),
            UploadedByUserId = current.UserId
        };
        db.AccountsEntryDocuments.Add(row);
        entry.Attachment = stored.StoredPath;
        await db.SaveChangesAsync(ct);
        return Map(row);
    }

    public async Task<IReadOnlyList<EntryAttachmentDto>> ListAttachmentsAsync(long id, CancellationToken ct = default)
    {
        if (!await db.RoznamchaEntries.AsNoTracking().AnyAsync(x => x.Id == id, ct))
            throw new KeyNotFoundException("Transaction was not found.");
        return await db.AccountsEntryDocuments.AsNoTracking().Where(x => x.RoznamchaEntryId == id)
            .OrderByDescending(x => x.UploadedOnUtc)
            .Select(x => new EntryAttachmentDto
            {
                Id = x.Id, EntryId = x.RoznamchaEntryId, FileName = x.FileName,
                ContentType = x.ContentType, FileSizeBytes = x.FileSizeBytes, UploadedOnUtc = x.UploadedOnUtc,
                DocumentReference = x.DocumentReference, Remarks = x.Remarks
            }).ToListAsync(ct);
    }

    public async Task DeleteAttachmentAsync(long attachmentId, CancellationToken ct = default)
    {
        var row = await db.AccountsEntryDocuments.FirstOrDefaultAsync(x => x.Id == attachmentId, ct)
            ?? throw new KeyNotFoundException("Attachment was not found.");
        db.AccountsEntryDocuments.Remove(row);
        await db.SaveChangesAsync(ct);
        await files.DeleteAccountsFileAsync(row.StoredPath, ct);
    }

    public async Task<(Stream Stream, string ContentType, string FileName)?> OpenAttachmentAsync(long attachmentId, CancellationToken ct = default)
    {
        var row = await db.AccountsEntryDocuments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == attachmentId, ct);
        if (row == null) return null;
        var stored = await files.OpenAccountsFileAsync(row.StoredPath, ct);
        return stored == null ? null : (stored.Value.Stream, row.ContentType ?? stored.Value.ContentType, row.FileName);
    }

    public async Task<IReadOnlyList<RoznamchaProcessLogDto>> ListProcessLogsAsync(long id, CancellationToken ct = default)
    {
        if (!await db.RoznamchaEntries.AsNoTracking().AnyAsync(x => x.Id == id, ct))
            throw new KeyNotFoundException("Transaction was not found.");
        return await db.RoznamchaEntryProcessLogs.AsNoTracking()
            .Where(x => x.RoznamchaEntryId == id)
            .OrderByDescending(x => x.ProcessedOnUtc)
            .Select(x => new RoznamchaProcessLogDto
            {
                Id = x.Id, EntryId = x.RoznamchaEntryId, Action = x.Action,
                Notes = x.Notes, ProcessedByUserId = x.ProcessedByUserId,
                ProcessedOnUtc = x.ProcessedOnUtc
            }).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<LibraryDocumentLinkDto>> ListLibraryDocumentsAsync(long id, CancellationToken ct = default)
    {
        if (!await db.RoznamchaEntries.AsNoTracking().AnyAsync(x => x.Id == id && !x.IsDeleted, ct))
            throw new KeyNotFoundException("Transaction was not found.");
        return await LinkedLibraryDocumentsQuery(id).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<LibraryDocumentLinkDto>> LinkLibraryDocumentsAsync(
        long id, IReadOnlyCollection<long> documentIds, CancellationToken ct = default)
    {
        var entry = await db.RoznamchaEntries.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct)
            ?? throw new KeyNotFoundException("Transaction was not found.");
        var requested = documentIds.Where(x => x > 0).Distinct().ToArray();
        await ValidateLibraryDocumentsAsync(requested, ct);
        await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
        var existing = await db.RoznamchaLibraryDocuments.Where(x => x.RoznamchaEntryId == id)
            .Select(x => x.LibraryDocumentId).ToListAsync(ct);
        foreach (var documentId in requested.Except(existing))
            db.RoznamchaLibraryDocuments.Add(NewLibraryLink(id, documentId));
        entry.IsFromLibrary = existing.Count > 0 || requested.Length > 0 || !string.IsNullOrWhiteSpace(entry.LibRef);
        await db.SaveChangesAsync(ct);
        if (transaction != null) await transaction.CommitAsync(ct);
        return await LinkedLibraryDocumentsQuery(id).ToListAsync(ct);
    }

    public async Task UnlinkLibraryDocumentAsync(long id, long documentId, CancellationToken ct = default)
    {
        var entry = await db.RoznamchaEntries.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct)
            ?? throw new KeyNotFoundException("Transaction was not found.");
        var link = await db.RoznamchaLibraryDocuments.FirstOrDefaultAsync(
            x => x.RoznamchaEntryId == id && x.LibraryDocumentId == documentId, ct)
            ?? throw new KeyNotFoundException("Linked library document was not found.");
        await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
        db.RoznamchaLibraryDocuments.Remove(link);
        entry.IsFromLibrary = await db.RoznamchaLibraryDocuments.AnyAsync(
                x => x.RoznamchaEntryId == id && x.LibraryDocumentId != documentId, ct)
            || !string.IsNullOrWhiteSpace(entry.LibRef);
        await db.SaveChangesAsync(ct);
        if (transaction != null) await transaction.CommitAsync(ct);
    }

    private async Task ValidateAsync(int entryTypeId, SaveRoznamchaEntryRequest request, CancellationToken ct)
    {
        if (request.FromAccountId <= 0 || request.ToAccountId <= 0)
            throw new InvalidOperationException("From and To accounts are required.");
        if (request.FromAccountId == request.ToAccountId)
            throw new InvalidOperationException("Cannot create transaction in the same account.");
        if (request.Amount < 0) throw new InvalidOperationException("Amount cannot be negative.");
        if (request.TransactionDate == default) throw new InvalidOperationException("Transaction date is required.");
        var count = await db.AccountsChartAccounts.AsNoTracking()
            .CountAsync(x => (x.Id == request.FromAccountId || x.Id == request.ToAccountId) && x.IsActive, ct);
        if (count != 2) throw new InvalidOperationException("From or To account was not found or is inactive.");
        if (request.CategoryId.HasValue && !await db.AccountsCategories.AsNoTracking().AnyAsync(x => x.Id == request.CategoryId && x.IsActive, ct))
            throw new InvalidOperationException("Category was not found or is inactive.");
        if (request.TransactionTypeId.HasValue && !await db.AccountsTransTypes.AsNoTracking().AnyAsync(x => x.Id == request.TransactionTypeId && x.IsActive, ct))
            throw new InvalidOperationException("Transaction type was not found or is inactive.");
        if (request.TransactionModeId.HasValue && !await db.AccountsTransModes.AsNoTracking().AnyAsync(x => x.Id == request.TransactionModeId && x.IsActive, ct))
            throw new InvalidOperationException("Transaction mode was not found or is inactive.");
        if (request.CurrencyId.HasValue && !await db.AccountsCurrencies.AsNoTracking().AnyAsync(x => x.Id == request.CurrencyId && x.IsActive, ct))
            throw new InvalidOperationException("Currency was not found or is inactive.");
        if (request.TaxTypeId.HasValue && !await db.AccountsTaxTypes.AsNoTracking().AnyAsync(x => x.Id == request.TaxTypeId && x.IsActive, ct))
            throw new InvalidOperationException("Tax type was not found or is inactive.");
        if (request.EntryStatusId.HasValue && !await db.AccountsEntryStatuses.AsNoTracking().AnyAsync(x => x.Id == request.EntryStatusId && x.IsActive, ct))
            throw new InvalidOperationException("Status was not found or is inactive.");
        if (request.ProjectId.HasValue && !await db.AccountsProjects.AsNoTracking().AnyAsync(x => x.Id == request.ProjectId && x.IsActive, ct))
            throw new InvalidOperationException("Project was not found or is inactive.");
        if (entryTypeId == 1 && request.ProductId.HasValue)
        {
            if (request.ProductId.Value <= 0)
                throw new InvalidOperationException("Selected product is invalid.");
            if (request.Quantity < 0)
                throw new InvalidOperationException("Purchase quantity cannot be negative.");
            if (!request.CurrencyId.HasValue)
                throw new InvalidOperationException("Currency is required when purchasing a product.");
            if (!await db.SaleRoznamchaProducts.AsNoTracking()
                    .AnyAsync(x => x.Id == request.ProductId.Value && x.IsActive && !x.IsDeleted, ct))
                throw new InvalidOperationException("Selected inventory product was not found or is inactive.");
        }
    }

    private async Task ValidateLibraryDocumentsAsync(IEnumerable<long> documentIds, CancellationToken ct)
    {
        var ids = documentIds.Where(x => x > 0).Distinct().ToArray();
        if (ids.Length == 0) return;
        var count = await db.LibraryDocuments.AsNoTracking()
            .CountAsync(x => ids.Contains(x.Id) && x.IsActive && x.AssetKind == "Document", ct);
        if (count != ids.Length)
            throw new InvalidOperationException("One or more library documents were not found, inactive, or outside this tenant.");
    }

    private async Task ReconcileLibraryDocumentsAsync(
        RoznamchaEntry entry, IEnumerable<long> documentIds, CancellationToken ct)
    {
        var requested = documentIds.Where(x => x > 0).Distinct().ToHashSet();
        var existing = await db.RoznamchaLibraryDocuments
            .Where(x => x.RoznamchaEntryId == entry.Id).ToListAsync(ct);
        db.RoznamchaLibraryDocuments.RemoveRange(existing.Where(x => !requested.Contains(x.LibraryDocumentId)));
        foreach (var documentId in requested.Except(existing.Select(x => x.LibraryDocumentId)))
            db.RoznamchaLibraryDocuments.Add(NewLibraryLink(entry.Id, documentId));
        entry.IsFromLibrary = requested.Count > 0 || !string.IsNullOrWhiteSpace(entry.LibRef);
    }

    private RoznamchaLibraryDocument NewLibraryLink(long entryId, long documentId) => new()
    {
        TenantId = current.TenantId,
        RoznamchaEntryId = entryId,
        LibraryDocumentId = documentId,
        CreatedByUserId = current.UserId,
        CreatedOnUtc = DateTime.UtcNow
    };

    private IQueryable<LibraryDocumentLinkDto> LinkedLibraryDocumentsQuery(long entryId) =>
        from link in db.RoznamchaLibraryDocuments.AsNoTracking()
        join document in db.LibraryDocuments.AsNoTracking() on link.LibraryDocumentId equals document.Id
        where link.RoznamchaEntryId == entryId
        orderby document.Id
        select new LibraryDocumentLinkDto
        {
            Id = document.Id,
            DocumentReference = document.DocumentReference ?? ("Doc-" + document.Id),
            Title = document.Title,
            OriginalFileName = document.OriginalFileName,
            ContentType = document.ContentType,
            FileSizeBytes = document.FileSizeBytes,
            DownloadUrl = $"/api/accounts/roznamcha/entry/{entryId}/library-documents/{document.Id}/download"
        };

    private async Task<bool> IsUsdCurrencyAsync(int? currencyId, CancellationToken ct) =>
        currencyId.HasValue && await db.AccountsCurrencies.AsNoTracking()
            .AnyAsync(x => x.Id == currencyId.Value && x.IsActive && x.Code == "USD", ct);

    private static void Apply(RoznamchaEntry row, SaveRoznamchaEntryRequest request, decimal net, decimal tax, bool isUsd)
    {
        row.OldRef = Clean(request.OldReferenceNo, 80);
        row.CategoryId = request.CategoryId;
        row.FromAccountId = request.FromAccountId;
        row.ToAccountId = request.ToAccountId;
        row.CustomerId = request.CustomerId;
        row.StaffId = request.StaffId;
        row.TransModeId = request.TransactionModeId;
        row.InstrumentDate = request.InstrumentDate;
        row.InstrumentNo = Clean(request.InstrumentNo, 100);
        row.Descriptions = Clean(request.Description, 2000);
        row.CurrencyId = request.CurrencyId;
        row.TaxTypeId = request.TaxTypeId;
        row.TaxRate = request.TaxRate;
        row.TaxAmt = tax;
        row.Adjustment = decimal.Round(request.Adjustment, 2);
        row.Amount = isUsd ? 0 : net;
        row.UsdAmount = isUsd ? net : 0;
        row.BalanceAmount = net + request.Adjustment;
        row.Qty = request.Quantity;
        row.Rate = request.Rate;
        row.EnterStatusId = request.EntryStatusId;
        row.Remarks = Clean(request.Remarks, 2000);
        row.BankRef = Clean(request.BankReference, 100);
        row.BankLtRef = Clean(request.BankLTReference, 100);
        row.TransDate = request.TransactionDate;
        row.IsShow = !request.IsShownHidden;
        row.IsSettled = request.IsSettled;
        row.LibRef = Clean(request.LibraryReference, 80);
        row.IsLedger = request.IsLedger;
        row.IsManual = request.IsManual;
        row.ProjectId = request.ProjectId;
    }

    private IQueryable<RoznamchaEntryDto> EntryQuery() =>
        from entry in db.RoznamchaEntries.AsNoTracking()
        join fromAccount in db.AccountsChartAccounts.AsNoTracking() on entry.FromAccountId equals fromAccount.Id into fromRows
        from fromAccount in fromRows.DefaultIfEmpty()
        join toAccount in db.AccountsChartAccounts.AsNoTracking() on entry.ToAccountId equals toAccount.Id into toRows
        from toAccount in toRows.DefaultIfEmpty()
        select new RoznamchaEntryDto
        {
            Id = entry.Id,
            SerialNo = entry.SNo,
            ReferenceNo = entry.Ref,
            OldReferenceNo = entry.OldRef,
            EntryTypeId = entry.TransTypeId ?? entry.RoznamchaTypeId ?? 0,
            CategoryId = entry.CategoryId,
            FromAccountId = entry.FromAccountId,
            FromAccountName = fromAccount == null ? null : fromAccount.AccountName,
            ToAccountId = entry.ToAccountId,
            ToAccountName = toAccount == null ? null : toAccount.AccountName,
            TransactionTypeId = entry.TransTypeId,
            TransactionModeId = entry.TransModeId,
            InstrumentDate = entry.InstrumentDate,
            InstrumentNo = entry.InstrumentNo,
            CurrencyId = entry.CurrencyId,
            TaxTypeId = entry.TaxTypeId,
            TaxRate = entry.TaxRate,
            EntryStatusId = entry.EnterStatusId,
            ProjectId = entry.ProjectId,
            Amount = entry.Amount ?? 0,
            UsdAmount = entry.UsdAmount ?? 0,
            GrossAmount = (entry.Amount ?? 0) + (entry.UsdAmount ?? 0) + (entry.TaxAmt ?? 0),
            TaxAmount = entry.TaxAmt ?? 0,
            Adjustment = entry.Adjustment ?? 0,
            Quantity = entry.Qty ?? 0,
            Rate = entry.Rate ?? 0,
            TransactionDate = entry.TransDate,
            Description = entry.Descriptions,
            Remarks = entry.Remarks,
            BankReference = entry.BankRef,
            BankLTReference = entry.BankLtRef,
            IsFromLibrary = entry.IsFromLibrary,
            LibraryReference = entry.LibRef,
            LibraryDocuments = (from link in db.RoznamchaLibraryDocuments
                                join document in db.LibraryDocuments on link.LibraryDocumentId equals document.Id
                                where link.RoznamchaEntryId == entry.Id
                                orderby document.Id
                                select new LibraryDocumentLinkDto
                                {
                                    Id = document.Id,
                                    DocumentReference = document.DocumentReference ?? ("Doc-" + document.Id),
                                    Title = document.Title,
                                    OriginalFileName = document.OriginalFileName,
                                    ContentType = document.ContentType,
                                    FileSizeBytes = document.FileSizeBytes,
                                    DownloadUrl = "/api/accounts/roznamcha/entry/" + entry.Id + "/library-documents/" + document.Id + "/download"
                                }).ToList(),
            IsApproved = entry.IsApproved,
            IsSettled = entry.IsSettled,
            IsShownHidden = !entry.IsShow,
            IsDeleted = entry.IsDeleted,
            IsLocked = entry.IsLocked,
            ProductId = db.SaleRoznamchaStockReceipts
                .Where(purchase => purchase.RoznamchaEntryId == entry.Id)
                .Select(purchase => (long?)purchase.ProductId)
                .FirstOrDefault()
        };

    private async Task AddProcessLogAsync(long id, string action, string? notes, CancellationToken ct)
    {
        db.RoznamchaEntryProcessLogs.Add(new RoznamchaEntryProcessLog
        {
            TenantId = current.TenantId,
            RoznamchaEntryId = id,
            Action = action,
            Notes = Clean(notes, 2000),
            ProcessedByUserId = current.UserId
        });
        await db.SaveChangesAsync(ct);
    }

    private async Task<int?> ResolveRoznamchaTypeIdAsync(int entryTypeId, CancellationToken ct)
    {
        var code = entryTypeId == 1 ? "PAYMENT" : "RECEIPT";
        return await db.AccountsRoznamchaTypes.AsNoTracking()
            .Where(x => x.IsActive && x.Code == code)
            .OrderByDescending(x => x.TenantId.HasValue)
            .Select(x => (int?)x.Id)
            .FirstOrDefaultAsync(ct);
    }

    private static EntryAttachmentDto Map(AccountsEntryDocument row) => new()
    {
        Id = row.Id, EntryId = row.RoznamchaEntryId, FileName = row.FileName,
        ContentType = row.ContentType, FileSizeBytes = row.FileSizeBytes, UploadedOnUtc = row.UploadedOnUtc,
        DocumentReference = row.DocumentReference, Remarks = row.Remarks
    };

    private async Task ApplyInventoryPurchaseFromPaymentAsync(
        int entryTypeId,
        RoznamchaEntry entry,
        SaveRoznamchaEntryRequest request,
        bool isUsd,
        CancellationToken ct)
    {
        if (entryTypeId != 1 || request.ProductId is not > 0 || !db.Database.IsRelational())
            return;
        if (!request.CurrencyId.HasValue)
            throw new InvalidOperationException("Currency is required when purchasing a product.");

        var quantity = decimal.Round(request.Quantity, 4, MidpointRounding.AwayFromZero);
        var total = decimal.Round(request.Amount, 2, MidpointRounding.AwayFromZero);
        var unitCost = request.Rate > 0
            ? decimal.Round(request.Rate, 2, MidpointRounding.AwayFromZero)
            : quantity > 0
                ? decimal.Round(total / quantity, 2, MidpointRounding.AwayFromZero)
                : total;
        try
        {
            await SpListQuery.ExecAsync<SaleRoznamchaCreatedId>(
                db,
                "EXEC dbo.usp_SaleRoznamcha_InventoryStockAdd @TenantId, @ProductId, @Reference, @Quantity, @UnitCost, @TotalPurchaseCost, @UsdAmount, @ExchangeRate, @CurrencyId, @FromAccountId, @ToAccountId, @PurchasedDate, @Description, @UserId, @RoznamchaEntryId",
                ct,
                SpListQuery.TenantId(current.TenantId),
                SpListQuery.BigInt("@ProductId", request.ProductId.Value),
                SpListQuery.NVarChar("@Reference", entry.Ref),
                SpListQuery.Decimal("@Quantity", quantity),
                SpListQuery.Decimal("@UnitCost", unitCost),
                SpListQuery.Decimal("@TotalPurchaseCost", total),
                SpListQuery.DecimalNullable("@UsdAmount", isUsd ? total : null, 2),
                SpListQuery.DecimalNullable("@ExchangeRate", null, 6),
                SpListQuery.Int("@CurrencyId", request.CurrencyId.Value),
                SpListQuery.Int("@FromAccountId", request.FromAccountId),
                SpListQuery.IntNullable("@ToAccountId", request.ToAccountId),
                SpListQuery.Date("@PurchasedDate", request.TransactionDate),
                SpListQuery.NVarChar("@Description", request.Description?.Trim()),
                SpListQuery.NVarChar("@UserId", current.UserId),
                SpListQuery.BigIntNullable("@RoznamchaEntryId", entry.Id));
            entry.IsLocked = true;
            await db.SaveChangesAsync(ct);
        }
        catch (SqlException ex) when (ex.Number is 51000 or 51001 or 2601 or 2627)
        {
            throw new InvalidOperationException(ex.Message, ex);
        }
    }

    private async Task EnsureNotInventoryPostedAsync(long entryId, string action, CancellationToken ct)
    {
        if (await db.SaleRoznamchaStockReceipts.AsNoTracking()
            .AnyAsync(x => x.RoznamchaEntryId == entryId, ct))
            throw new InvalidOperationException(
                $"This payment was posted from an inventory purchase and cannot be {action} here.");
    }

    private static void EnsureEntryType(int entryTypeId)
    {
        if (entryTypeId is not (1 or 2)) throw new InvalidOperationException("Entry type must be Payment (1) or Receipt (2).");
    }

    private static string? Clean(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, max)];
}
