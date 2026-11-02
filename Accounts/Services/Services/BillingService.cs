using Accounts.Data;
using Accounts.DTOs;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Services;

public sealed class BillingService : IBillingService
{
    private readonly ApplicationDbContext _db;

    public BillingService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<BillingReceiptRozListRow>> ListBillingReceiptRozAsync(
        int tenantId,
        DateOnly? dateFrom,
        DateOnly? dateTo,
        CancellationToken ct = default)
    {
        return await SpListQuery.ExecAsync<BillingReceiptRozListRow>(
            _db,
            "EXEC dbo.usp_Billing_ReceiptRoz_List @TenantId, @DateFrom, @DateTo",
            ct,
            SpListQuery.TenantId(tenantId),
            SpListQuery.DateNullable("@DateFrom", dateFrom),
            SpListQuery.DateNullable("@DateTo", dateTo));
    }

    public async Task<int> SettleReceiptRozAsync(
        int tenantId,
        BillingSettleRequest request,
        string? userId,
        CancellationToken ct = default)
    {
        IQueryable<RoznamchaEntry> query = _db.RoznamchaEntries
            .Where(x => x.TenantId == tenantId && !x.IsDeleted && !x.IsSettled);

        if (request.Ids.Count > 0)
        {
            query = query.Where(x => request.Ids.Contains(x.Id));
        }
        else if (request.DateFrom.HasValue && request.DateTo.HasValue)
        {
            query = query.Where(x => x.TransDate >= request.DateFrom.Value && x.TransDate <= request.DateTo.Value);
        }
        else
        {
            return 0;
        }

        var entries = await query.ToListAsync(ct);
        if (entries.Count == 0) return 0;

        foreach (var entry in entries)
        {
            entry.IsSettled = true;
            if (!string.IsNullOrWhiteSpace(request.Remarks))
            {
                entry.Remarks = string.IsNullOrWhiteSpace(entry.Remarks)
                    ? request.Remarks
                    : $"{entry.Remarks} | {request.Remarks}";
            }
        }

        await _db.SaveChangesAsync(ct);
        return entries.Count;
    }

    public async Task<int> UnsettleReceiptRozAsync(
        int tenantId,
        List<long> ids,
        string? userId,
        CancellationToken ct = default)
    {
        if (ids.Count == 0) return 0;
        var entries = await _db.RoznamchaEntries
            .Where(x => x.TenantId == tenantId && ids.Contains(x.Id))
            .ToListAsync(ct);

        foreach (var entry in entries)
        {
            entry.IsSettled = false;
        }

        await _db.SaveChangesAsync(ct);
        return entries.Count;
    }

    public async Task<bool> UpdateStatusAsync(
        int tenantId,
        long id,
        int statusId,
        string? remarks,
        string? userId,
        CancellationToken ct = default)
    {
        var entry = await _db.RoznamchaEntries.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, ct);
        if (entry == null) return false;

        entry.EnterStatusId = statusId;
        if (!string.IsNullOrWhiteSpace(remarks))
        {
            entry.Remarks = remarks;
        }

        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> TransferAccountAsync(
        int tenantId,
        long id,
        int transferAccountId,
        string? userId,
        CancellationToken ct = default)
    {
        var entry = await _db.RoznamchaEntries.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, ct);
        if (entry == null) return false;

        var targetAccount = await _db.AccountsChartAccounts
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == transferAccountId, ct);
        if (targetAccount == null) return false;

        entry.FromAccountId = transferAccountId;
        if (targetAccount.CategoryId.HasValue)
        {
            entry.CategoryId = targetAccount.CategoryId.Value;
        }

        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> AddProcessLogAsync(
        int tenantId,
        BillingProcessLogRequest request,
        string? userId,
        CancellationToken ct = default)
    {
        var entry = await _db.RoznamchaEntries.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == request.EntryId, ct);
        if (entry == null) return false;

        if (request.StatusId.HasValue)
        {
            entry.EnterStatusId = request.StatusId.Value;
        }

        if (!string.IsNullOrWhiteSpace(request.CheckNo))
        {
            entry.EobCheckNo = request.CheckNo.Trim();
        }

        if (!string.IsNullOrWhiteSpace(request.Remarks))
        {
            entry.Remarks = request.Remarks.Trim();
        }

        // Store log in RoznamchaEntryProcessLogs if available
        var log = new RoznamchaEntryProcessLog
        {
            TenantId = tenantId,
            RoznamchaEntryId = entry.Id,
            Action = "UpdateReceipt",
            Notes = request.Remarks,
            ProcessedByUserId = userId,
            ProcessedOnUtc = DateTime.UtcNow
        };
        _db.RoznamchaEntryProcessLogs.Add(log);

        await _db.SaveChangesAsync(ct);
        return true;
    }
}

