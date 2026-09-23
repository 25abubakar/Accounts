using Accounts.Data;
using Accounts.DTOs;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Services;

public sealed class PayableReceivableService(
    ApplicationDbContext db,
    ICurrentUserService current,
    IRoznamchaService roznamcha) : IPayableReceivableService
{
    public async Task<IReadOnlyList<RecurringTransactionDto>> ListAsync(string? kind, CancellationToken ct = default)
    {
        var normalized = NormalizeKind(kind, allowAll: true);
        var result = new List<RecurringTransactionDto>();
        if (normalized is "all" or "payable") result.AddRange(await PayableQuery().ToListAsync(ct));
        if (normalized is "all" or "receivable") result.AddRange(await ReceivableQuery().ToListAsync(ct));
        return result.OrderBy(x => x.DueDate).ThenBy(x => x.Id).ToList();
    }

    public Task<RecurringTransactionDto?> GetAsync(int id, string kind, CancellationToken ct = default) =>
        NormalizeKind(kind) == "payable"
            ? PayableQuery().FirstOrDefaultAsync(x => x.Id == id, ct)
            : ReceivableQuery().FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<RecurringTransactionDto> CreateAsync(CreateRecurringTransactionRequest request, CancellationToken ct = default)
    {
        var kind = NormalizeKind(request.Kind);
        await ValidateAsync(request, ct);
        if (kind == "payable")
        {
            var row = new ReminderPayable { TenantId = current.TenantId, CreatedByUserId = current.UserId };
            Apply(row, request);
            db.ReminderPayables.Add(row);
            await db.SaveChangesAsync(ct);
            if (string.IsNullOrWhiteSpace(row.Ref)) { row.Ref = $"PAY-{row.Id}"; await db.SaveChangesAsync(ct); }
            return await GetAsync(row.Id, kind, ct) ?? throw new InvalidOperationException("Saved payable could not be reloaded.");
        }
        else
        {
            var row = new ReminderReceivable { TenantId = current.TenantId, CreatedByUserId = current.UserId };
            Apply(row, request);
            db.ReminderReceivables.Add(row);
            await db.SaveChangesAsync(ct);
            if (string.IsNullOrWhiteSpace(row.Ref)) { row.Ref = $"REC-{row.Id}"; await db.SaveChangesAsync(ct); }
            return await GetAsync(row.Id, kind, ct) ?? throw new InvalidOperationException("Saved receivable could not be reloaded.");
        }
    }

    public async Task<RecurringTransactionDto> UpdateAsync(int id, CreateRecurringTransactionRequest request, CancellationToken ct = default)
    {
        var kind = NormalizeKind(request.Kind);
        await ValidateAsync(request, ct);
        if (kind == "payable")
        {
            var row = await db.ReminderPayables.FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new KeyNotFoundException("Payable was not found.");
            Apply(row, request);
            row.UpdatedByUserId = current.UserId;
            row.UpdatedOnUtc = DateTime.UtcNow;
        }
        else
        {
            var row = await db.ReminderReceivables.FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new KeyNotFoundException("Receivable was not found.");
            Apply(row, request);
            row.UpdatedByUserId = current.UserId;
            row.UpdatedOnUtc = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, kind, ct) ?? throw new InvalidOperationException("Updated recurring entry could not be reloaded.");
    }

    public async Task<RecurringTransactionDto> UpdateStatusAsync(int id, string kind, bool isInactive, CancellationToken ct = default)
    {
        kind = NormalizeKind(kind);
        if (kind == "payable")
        {
            var row = await db.ReminderPayables.FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new KeyNotFoundException("Payable was not found.");
            row.IsInactive = isInactive;
            row.IsActive = !isInactive;
            row.UpdatedByUserId = current.UserId;
            row.UpdatedOnUtc = DateTime.UtcNow;
        }
        else
        {
            var row = await db.ReminderReceivables.FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new KeyNotFoundException("Receivable was not found.");
            row.IsInactive = isInactive;
            row.IsActive = !isInactive;
            row.UpdatedByUserId = current.UserId;
            row.UpdatedOnUtc = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, kind, ct) ?? throw new InvalidOperationException("Updated recurring entry could not be reloaded.");
    }

    public async Task<RecurringTransactionDto> SettleAsync(int id, string kind, SettleRecurringTransactionRequest request, CancellationToken ct = default)
    {
        kind = NormalizeKind(kind);
        var currentRow = await GetAsync(id, kind, ct) ?? throw new KeyNotFoundException("Recurring entry was not found.");
        if (currentRow.IsInactive) throw new InvalidOperationException("Inactive recurring entry cannot be settled.");
        var settledOn = request.PaidOn ?? DateOnly.FromDateTime(DateTime.UtcNow);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (!currentRow.IsNotRoznamcha)
        {
            if (!currentRow.FromAccountId.HasValue || !currentRow.ToAccountId.HasValue)
                throw new InvalidOperationException("From and To accounts are required before settlement.");
            await roznamcha.CreateAsync(kind == "payable" ? 1 : 2, new SaveRoznamchaEntryRequest
            {
                CategoryId = currentRow.CategoryId,
                FromAccountId = currentRow.FromAccountId.Value,
                ToAccountId = currentRow.ToAccountId.Value,
                TransactionTypeId = kind == "payable" ? 1 : 2,
                TransactionModeId = request.TransactionModeId,
                CurrencyId = currentRow.CurrencyId,
                Description = $"Recurring {kind}: {currentRow.ReferenceNo}",
                Amount = currentRow.Amount,
                TransactionDate = settledOn,
                Remarks = request.Remarks,
                IsSettled = true
            }, ct);
        }

        if (kind == "payable")
        {
            var row = await db.ReminderPayables.FirstAsync(x => x.Id == id, ct);
            row.LastPaidDate = settledOn;
            row.PaidOn = settledOn;
            row.UpdatedByUserId = current.UserId;
            row.UpdatedOnUtc = DateTime.UtcNow;
        }
        else
        {
            var row = await db.ReminderReceivables.FirstAsync(x => x.Id == id, ct);
            row.LastPaidDate = settledOn;
            row.PaidOn = settledOn;
            row.UpdatedByUserId = current.UserId;
            row.UpdatedOnUtc = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(ct);
        if (request.GenerateNext) await GenerateNextInternalAsync(id, kind, ct);
        await transaction.CommitAsync(ct);
        return await GetAsync(id, kind, ct) ?? throw new InvalidOperationException("Settled recurring entry could not be reloaded.");
    }

    public async Task<RecurringTransactionDto> GenerateNextAsync(int id, string kind, CancellationToken ct = default) =>
        await GenerateNextInternalAsync(id, NormalizeKind(kind), ct);

    private async Task<RecurringTransactionDto> GenerateNextInternalAsync(int id, string kind, CancellationToken ct)
    {
        var source = await GetAsync(id, kind, ct) ?? throw new KeyNotFoundException("Recurring entry was not found.");
        if (!source.DueDate.HasValue) throw new InvalidOperationException("Due date is required to generate the next entry.");
        var frequency = (source.Frequency ?? string.Empty).Trim().ToUpperInvariant();
        var months = frequency switch
        {
            "MONTHLY" or "PM" => 1,
            "QUARTERLY" => 3,
            "YEARLY" or "ANNUALLY" or "PY" => 12,
            _ => throw new InvalidOperationException("Only monthly, quarterly and yearly frequencies can generate a next entry.")
        };
        var nextDue = source.DueDate.Value.AddMonths(months);
        var created = await CreateAsync(new CreateRecurringTransactionRequest
        {
            Kind = kind,
            ReferenceNo = null,
            AccountTypeId = source.AccountTypeId,
            TypeId = source.TypeId,
            InvoiceTypeId = source.InvoiceTypeId,
            CategoryId = source.CategoryId,
            FromAccountId = source.FromAccountId,
            ToCategoryId = source.ToCategoryId,
            ToAccountId = source.ToAccountId,
            FrequencyId = source.FrequencyId,
            TransactionTypeId = kind == "payable" ? 1 : 2,
            DueDate = nextDue,
            ReminderDays = source.ReminderDays,
            Amount = source.Amount,
            CurrencyId = source.CurrencyId,
            StatusId = source.StatusId,
            Attachment = source.Attachment,
            Remarks = source.Remarks,
            IsNotRoznamcha = source.IsNotRoznamcha
        }, ct);
        return created;
    }

    private async Task ValidateAsync(CreateRecurringTransactionRequest request, CancellationToken ct)
    {
        if (request.Amount < 0) throw new InvalidOperationException("Amount cannot be negative.");
        if (request.ReminderDays is < 0 or > 3650) throw new InvalidOperationException("Reminder days must be between 0 and 3650.");
        var ids = new[] { request.FromAccountId, request.ToAccountId }.Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToList();
        if (ids.Count > 0 && await db.AccountsChartAccounts.AsNoTracking().CountAsync(x => ids.Contains(x.Id) && x.IsActive, ct) != ids.Count)
            throw new InvalidOperationException("One or more selected accounts are invalid or inactive.");
        if (request.FromAccountId.HasValue && request.FromAccountId == request.ToAccountId)
            throw new InvalidOperationException("From and To accounts cannot be the same.");
    }

    private IQueryable<RecurringTransactionDto> PayableQuery() =>
        from row in db.ReminderPayables.AsNoTracking()
        join frequency in db.FrequencyTypes.AsNoTracking() on row.FrequencyTypeId equals frequency.Id into frequencies
        from frequency in frequencies.DefaultIfEmpty()
        select new RecurringTransactionDto
        {
            Id = row.Id, Kind = "payable", ReferenceNo = row.Ref, AccountTypeId = row.AccountTypeId,
            TypeId = row.ReminderTypeId, InvoiceTypeId = row.InvoiceTypeId, CategoryId = row.CategoryId,
            FromAccountId = row.FromAccountId, ToCategoryId = row.ToCategoryId, ToAccountId = row.ToAccountId,
            FrequencyId = row.FrequencyTypeId, Frequency = frequency == null ? null : frequency.Name,
            DueDate = row.DueDate, LastPaidDate = row.LastPaidDate, PaidOn = row.PaidOn,
            RemindDate = row.RemindDate, ReminderDays = row.RemindDay ?? 0, Amount = row.Amount,
            CurrencyId = row.CurrencyId, StatusId = row.StatusId, Attachment = row.Attachment, Remarks = row.Remarks,
            IsInactive = row.IsInactive || !row.IsActive, IsNotRoznamcha = row.IsNotRoznamcha
        };

    private IQueryable<RecurringTransactionDto> ReceivableQuery() =>
        from row in db.ReminderReceivables.AsNoTracking()
        join frequency in db.FrequencyTypes.AsNoTracking() on row.FrequencyTypeId equals frequency.Id into frequencies
        from frequency in frequencies.DefaultIfEmpty()
        select new RecurringTransactionDto
        {
            Id = row.Id, Kind = "receivable", ReferenceNo = row.Ref, AccountTypeId = row.AccountTypeId,
            TypeId = row.ReminderTypeId, InvoiceTypeId = row.InvoiceTypeId, CategoryId = row.CategoryId,
            FromAccountId = row.FromAccountId, ToCategoryId = row.ToCategoryId, ToAccountId = row.ToAccountId,
            FrequencyId = row.FrequencyTypeId, Frequency = frequency == null ? null : frequency.Name,
            DueDate = row.DueDate, LastPaidDate = row.LastPaidDate, PaidOn = row.PaidOn,
            RemindDate = row.RemindDate, ReminderDays = row.RemindDay ?? 0, Amount = row.Amount,
            CurrencyId = row.CurrencyId, StatusId = row.StatusId, Attachment = row.Attachment, Remarks = row.Remarks,
            IsInactive = row.IsInactive || !row.IsActive, IsNotRoznamcha = row.IsNotRoznamcha
        };

    private static void Apply(ReminderPayable row, CreateRecurringTransactionRequest request)
    {
        row.Ref = Clean(request.ReferenceNo, 80);
        row.AccountTypeId = request.AccountTypeId;
        row.ReminderTypeId = request.TypeId;
        row.InvoiceTypeId = request.InvoiceTypeId;
        row.CategoryId = request.CategoryId;
        row.FromAccountId = request.FromAccountId;
        row.ToCategoryId = request.ToCategoryId;
        row.ToAccountId = request.ToAccountId;
        row.FrequencyTypeId = request.FrequencyId;
        row.TransTypeId = request.TransactionTypeId ?? 1;
        row.DueDate = request.DueDate;
        row.LastPaidDate = request.LastPaidDate;
        row.PaidOn = request.PaidOn;
        row.RemindDay = request.ReminderDays;
        row.RemindDate = request.DueDate?.AddDays(-request.ReminderDays);
        row.StatusId = request.StatusId;
        row.Amount = decimal.Round(request.Amount, 2);
        row.CurrencyId = request.CurrencyId;
        row.Attachment = Clean(request.Attachment, 500);
        row.Remarks = Clean(request.Remarks, 2000);
        row.IsInactive = request.IsInactive;
        row.IsActive = !request.IsInactive;
        row.IsNotRoznamcha = request.IsNotRoznamcha;
        row.InAlertRoznamcha = !request.IsNotRoznamcha;
    }

    private static void Apply(ReminderReceivable row, CreateRecurringTransactionRequest request)
    {
        row.Ref = Clean(request.ReferenceNo, 80);
        row.AccountTypeId = request.AccountTypeId;
        row.ReminderTypeId = request.TypeId;
        row.InvoiceTypeId = request.InvoiceTypeId;
        row.CategoryId = request.CategoryId;
        row.FromAccountId = request.FromAccountId;
        row.ToCategoryId = request.ToCategoryId;
        row.ToAccountId = request.ToAccountId;
        row.FrequencyTypeId = request.FrequencyId;
        row.TransTypeId = request.TransactionTypeId ?? 2;
        row.DueDate = request.DueDate;
        row.LastPaidDate = request.LastPaidDate;
        row.PaidOn = request.PaidOn;
        row.RemindDay = request.ReminderDays;
        row.RemindDate = request.DueDate?.AddDays(-request.ReminderDays);
        row.StatusId = request.StatusId;
        row.Amount = decimal.Round(request.Amount, 2);
        row.CurrencyId = request.CurrencyId;
        row.Attachment = Clean(request.Attachment, 500);
        row.Remarks = Clean(request.Remarks, 2000);
        row.IsInactive = request.IsInactive;
        row.IsActive = !request.IsInactive;
        row.IsNotRoznamcha = request.IsNotRoznamcha;
        row.AlertRoznamcha = !request.IsNotRoznamcha;
    }

    private static string NormalizeKind(string? kind, bool allowAll = false)
    {
        var value = (kind ?? (allowAll ? "all" : string.Empty)).Trim().ToLowerInvariant();
        if (value is "payable" or "receivable" || allowAll && value == "all") return value;
        throw new InvalidOperationException("Kind must be 'payable' or 'receivable'.");
    }

    private static string? Clean(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, max)];
}
