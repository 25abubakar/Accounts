using Accounts.Data;
using Accounts.DTOs;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Services;

public sealed class BillingRozExcelService : IBillingRozExcelService
{
    private readonly ApplicationDbContext _db;
    private readonly IReferenceGeneratorService _references;

    public BillingRozExcelService(ApplicationDbContext db, IReferenceGeneratorService references)
    {
        _db = db;
        _references = references;
    }

    public async Task<int> UploadBillingExcelAsync(int tenantId, IFormFile file, string? userId, CancellationToken ct = default)
    {
        if (file == null || file.Length == 0) return 0;
        int count = 0;

        using var stream = file.OpenReadStream();
        using var reader = new System.IO.StreamReader(stream);

        // Parse CSV or header line
        string? header = await reader.ReadLineAsync(ct);
        if (header == null) return 0;

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrWhiteSpace(line)) continue;
            var parts = line.Split(',');
            if (parts.Length < 3) continue;

            // Typical columns: Practice/Ref, Ins_Name, ChequeNo, Amount, Date
            var insName = parts.Length > 1 ? parts[1].Trim() : string.Empty;
            var checkNo = parts.Length > 2 ? parts[2].Trim() : string.Empty;
            decimal.TryParse(parts.Length > 3 ? parts[3].Trim() : "0", out var amount);
            DateTime.TryParse(parts.Length > 4 ? parts[4].Trim() : "", out var transDate);
            if (transDate == default) transDate = DateTime.UtcNow;

            var refNo = await _references.ReceiptReferenceAsync(tenantId, ct);

            var row = new BillingRoznamchaStaging
            {
                TenantId = tenantId,
                OrganizationId = tenantId,
                Ins_Name = insName,
                ChequeNo = checkNo,
                Amount = amount,
                Trans_Date = transDate,
                Ref = refNo,
                isSettled = false,
                CreatedBy = userId,
                CreatedDate = DateTime.UtcNow
            };
            _db.BillingRoznamchas.Add(row);
            count++;
        }

        await _db.SaveChangesAsync(ct);
        return count;
    }

    public async Task<IReadOnlyList<BillingRozExcelRowDto>> ListStagedAsync(
        int tenantId,
        DateOnly? dateFrom,
        DateOnly? dateTo,
        bool? isSettled = null,
        CancellationToken ct = default)
    {
        return await SpListQuery.ExecAsync<BillingRozExcelRowDto>(
            _db,
            "EXEC dbo.usp_Billing_RozExcel_Staging_List @TenantId, @DateFrom, @DateTo, @IsSettled",
            ct,
            SpListQuery.TenantId(tenantId),
            SpListQuery.DateNullable("@DateFrom", dateFrom),
            SpListQuery.DateNullable("@DateTo", dateTo),
            SpListQuery.BitNullable("@IsSettled", isSettled));
    }

    public async Task<int> SettleStagedIntoRoznamchaAsync(
        int tenantId,
        List<int> ids,
        string? userId,
        CancellationToken ct = default)
    {
        IQueryable<BillingRoznamchaStaging> query = _db.BillingRoznamchas
            .Where(x => (x.TenantId == tenantId || x.OrganizationId == tenantId) && (x.isSettled != true));

        if (ids.Count > 0)
        {
            query = query.Where(x => ids.Contains(x.Id));
        }

        var stagedRows = await query.ToListAsync(ct);
        if (stagedRows.Count == 0) return 0;

        // Resolve Billing Category
        var billingCategory = await _db.AccountsCategories.AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && (x.Name.ToLower() == "billing" || x.Code.ToLower() == "billing"), ct);

        var receiptType = await _db.AccountsRoznamchaTypes.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Code == "RECEIPT", ct);

        // Fallback default bank or cash account
        var defaultAccount = await _db.AccountsChartAccounts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.IsActive, ct);

        int settledCount = 0;
        foreach (var staged in stagedRows)
        {
            var rozEntry = new RoznamchaEntry
            {
                TenantId = tenantId,
                RoznamchaTypeId = receiptType?.Id ?? 2,
                CategoryId = billingCategory?.Id,
                FromAccountId = defaultAccount?.Id,
                Ref = staged.Ref ?? await _references.ReceiptReferenceAsync(tenantId, ct),
                Amount = staged.Amount,
                TransDate = DateOnly.FromDateTime(staged.Trans_Date ?? DateTime.UtcNow),
                EobCheckNo = staged.ChequeNo,
                Descriptions = $"Imported from Billing Roz Excel - {staged.Ins_Name}",
                IsSettled = true,
                IsShow = true,
                CreatedOnUtc = DateTime.UtcNow
            };
            _db.RoznamchaEntries.Add(rozEntry);

            staged.isSettled = true;
            staged.UpdatedBy = userId;
            staged.UpdatedDate = DateTime.UtcNow;
            settledCount++;
        }

        await _db.SaveChangesAsync(ct);
        return settledCount;
    }

    public async Task<bool> DeleteStagedAsync(int tenantId, int id, CancellationToken ct = default)
    {
        var row = await _db.BillingRoznamchas
            .FirstOrDefaultAsync(x => (x.TenantId == tenantId || x.OrganizationId == tenantId) && x.Id == id && x.isSettled != true, ct);
        if (row == null) return false;

        _db.BillingRoznamchas.Remove(row);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<BillingRozExcelSummaryDto> GetSummaryAsync(int tenantId, CancellationToken ct = default)
    {
        var rows = await _db.BillingRoznamchas.AsNoTracking()
            .Where(x => x.TenantId == tenantId || x.OrganizationId == tenantId)
            .ToListAsync(ct);

        var total = rows.Count;
        var settled = rows.Count(x => x.isSettled == true);
        var pending = total - settled;
        var totalAmt = rows.Sum(x => x.Amount ?? 0);
        var settledAmt = rows.Where(x => x.isSettled == true).Sum(x => x.Amount ?? 0);
        var pendingAmt = totalAmt - settledAmt;

        return new BillingRozExcelSummaryDto
        {
            TotalImported = total,
            PendingSettlement = pending,
            SettledCount = settled,
            TotalAmount = totalAmt,
            SettledAmount = settledAmt,
            PendingAmount = pendingAmt
        };
    }
}

