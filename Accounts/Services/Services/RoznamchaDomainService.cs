using Accounts.Data;
using Accounts.DTOs;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Microsoft.AspNetCore.Http;
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
        await ValidateAsync(request, ct);
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
        db.RoznamchaEntries.Add(row);
        await db.SaveChangesAsync(ct);
        await AddProcessLogAsync(row.Id, "Created", null, ct);
        return await GetAsync(row.Id, ct) ?? throw new InvalidOperationException("Saved transaction could not be reloaded.");
    }

    public async Task<RoznamchaEntryDto> UpdateAsync(long id, int entryTypeId, SaveRoznamchaEntryRequest request, CancellationToken ct = default)
    {
        EnsureEntryType(entryTypeId);
        var row = await db.RoznamchaEntries.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct)
            ?? throw new KeyNotFoundException("Transaction was not found.");
        if (row.IsLocked) throw new InvalidOperationException("Locked transaction cannot be edited.");
        await ValidateAsync(request, ct);
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
        await db.SaveChangesAsync(ct);
        await AddProcessLogAsync(row.Id, "Updated", null, ct);
        return await GetAsync(row.Id, ct) ?? throw new InvalidOperationException("Updated transaction could not be reloaded.");
    }

    public async Task SoftDeleteAsync(long id, CancellationToken ct = default)
    {
        var row = await db.RoznamchaEntries.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new KeyNotFoundException("Transaction was not found.");
        if (row.IsLocked) throw new InvalidOperationException("Locked transaction cannot be deleted.");
        row.IsDeleted = true;
        row.UpdatedByUserId = current.UserId;
        row.UpdatedOnUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await AddProcessLogAsync(row.Id, "SoftDeleted", null, ct);
    }

    public async Task<RoznamchaEntryDto> UpdateStatusAsync(long id, UpdateRoznamchaStatusRequest request, CancellationToken ct = default)
    {
        var row = await db.RoznamchaEntries.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct)
            ?? throw new KeyNotFoundException("Transaction was not found.");
        if (request.EntryStatusId.HasValue) row.EnterStatusId = request.EntryStatusId;
        if (request.IsSettled.HasValue) row.IsSettled = request.IsSettled.Value;
        if (request.IsShownHidden.HasValue) row.IsShow = !request.IsShownHidden.Value;
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
                ContentType = x.ContentType, FileSizeBytes = x.FileSizeBytes, UploadedOnUtc = x.UploadedOnUtc
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

    private async Task ValidateAsync(SaveRoznamchaEntryRequest request, CancellationToken ct)
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
    }

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
        row.IsFromLibrary = request.IsFromLibrary;
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
            Amount = entry.Amount ?? 0,
            UsdAmount = entry.UsdAmount ?? 0,
            TaxAmount = entry.TaxAmt ?? 0,
            Adjustment = entry.Adjustment ?? 0,
            TransactionDate = entry.TransDate,
            Description = entry.Descriptions,
            Remarks = entry.Remarks,
            IsApproved = entry.IsApproved,
            IsSettled = entry.IsSettled,
            IsShownHidden = !entry.IsShow,
            IsDeleted = entry.IsDeleted,
            IsLocked = entry.IsLocked
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
        ContentType = row.ContentType, FileSizeBytes = row.FileSizeBytes, UploadedOnUtc = row.UploadedOnUtc
    };

    private static void EnsureEntryType(int entryTypeId)
    {
        if (entryTypeId is not (1 or 2)) throw new InvalidOperationException("Entry type must be Payment (1) or Receipt (2).");
    }

    private static string? Clean(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, max)];
}
