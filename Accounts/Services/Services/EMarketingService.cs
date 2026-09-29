using Accounts.Data;
using Accounts.DTOs;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Services;

public sealed class EMarketingService(
    ApplicationDbContext db,
    ICurrentUserService current) : IEMarketingService
{
    public async Task<IReadOnlyList<EMarketingStockInfoDto>> ListStockInfoAsync(
        int accountId,
        DateOnly? dateFrom,
        DateOnly? dateTo,
        CancellationToken ct = default) =>
        await SpListQuery.ExecAsync<EMarketingStockInfoDto>(
            db,
            "EXEC dbo.usp_EMarketing_StockInfoList @TenantId, @AccountId, @DateFrom, @DateTo",
            ct,
            SpListQuery.TenantId(current.TenantId),
            SpListQuery.Int("@AccountId", accountId),
            SpListQuery.DateNullable("@DateFrom", dateFrom),
            SpListQuery.DateNullable("@DateTo", dateTo));

    public async Task<IReadOnlyList<EMarketingSalesRozDto>> ListSalesRozAsync(
        int? accountId,
        DateOnly? dateFrom,
        DateOnly? dateTo,
        CancellationToken ct = default) =>
        await SpListQuery.ExecAsync<EMarketingSalesRozDto>(
            db,
            "EXEC dbo.usp_EMarketing_SalesRozList @TenantId, @AccountId, @DateFrom, @DateTo",
            ct,
            SpListQuery.TenantId(current.TenantId),
            SpListQuery.IntNullable("@AccountId", accountId),
            SpListQuery.DateNullable("@DateFrom", dateFrom),
            SpListQuery.DateNullable("@DateTo", dateTo));

    public Task<EMarketingSalesRozDto?> GetSalesRozAsync(long id, CancellationToken ct = default) =>
        MapQuery().FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<EMarketingSalesRozDto> SaveSalesRozAsync(
        long? id,
        SaveEMarketingSalesRozRequest request,
        CancellationToken ct = default)
    {
        if (request.AccountId <= 0)
            throw new InvalidOperationException("Account is required.");
        if (request.TransDate == default)
            throw new InvalidOperationException("TransDate is required.");

        var accountExists = await db.AccountsChartAccounts.AsNoTracking()
            .AnyAsync(x => x.Id == request.AccountId, ct);
        if (!accountExists)
            throw new InvalidOperationException("Account was not found.");

        EMarketingSalesRozEntry row;
        if (id.HasValue)
        {
            row = await db.EMarketingSalesRozEntries.FirstOrDefaultAsync(x => x.Id == id.Value && !x.IsDeleted, ct)
                ?? throw new KeyNotFoundException("Sales (Roz) entry was not found.");
            row.UpdatedByUserId = current.UserId;
            row.UpdatedOnUtc = DateTime.UtcNow;
        }
        else
        {
            row = new EMarketingSalesRozEntry
            {
                TenantId = current.TenantId,
                CreatedByUserId = current.UserId,
                CreatedOnUtc = DateTime.UtcNow
            };
            db.EMarketingSalesRozEntries.Add(row);
        }

        Apply(row, request);
        await db.SaveChangesAsync(ct);

        if (string.IsNullOrWhiteSpace(row.Ref))
        {
            row.Ref = $"SR-{row.Id}";
            await db.SaveChangesAsync(ct);
        }

        return await GetSalesRozAsync(row.Id, ct)
            ?? throw new InvalidOperationException("Saved sales entry could not be reloaded.");
    }

    public async Task DeleteSalesRozAsync(long id, CancellationToken ct = default)
    {
        var row = await db.EMarketingSalesRozEntries.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct)
            ?? throw new KeyNotFoundException("Sales (Roz) entry was not found.");
        row.IsDeleted = true;
        row.UpdatedByUserId = current.UserId;
        row.UpdatedOnUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private IQueryable<EMarketingSalesRozDto> MapQuery() =>
        from s in db.EMarketingSalesRozEntries.AsNoTracking()
        join a in db.AccountsChartAccounts.AsNoTracking() on s.AccountId equals a.Id
        where !s.IsDeleted
        select new EMarketingSalesRozDto
        {
            Id = s.Id,
            Ref = s.Ref,
            AccountId = s.AccountId,
            AcctName = a.AccountName,
            PlatformName = s.PlatformName,
            TransTypeId = s.TransTypeId,
            TransTypeName = s.TransTypeName,
            SalesTypeId = s.SalesTypeId,
            SalesTypeName = s.SalesTypeName,
            Qty = s.Qty,
            AmzProRef = s.AmzProRef,
            TotProCharges = s.TotProCharges,
            TotPromotion = s.TotPromotion,
            TotProRebate = s.TotProRebate,
            AmazonFee = s.AmazonFee,
            Descriptions = s.Descriptions,
            OrderId = s.OrderId,
            Other = s.Other,
            Amount = s.Amount,
            PurchaseAmount = s.PurchaseAmount,
            SaleAmount = s.SaleAmount,
            QtyPurchase = s.QtyPurchase,
            QtySale = s.QtySale,
            StatusId = s.StatusId,
            StatusName = s.StatusName,
            TransDate = s.TransDate,
            DocName = s.DocName,
            AttachmentPath = s.AttachmentPath,
            Remarks = s.Remarks
        };

    private static void Apply(EMarketingSalesRozEntry row, SaveEMarketingSalesRozRequest request)
    {
        var salesType = (request.SalesTypeName ?? string.Empty).Trim();
        var isPurchase = salesType.Contains("purchase", StringComparison.OrdinalIgnoreCase)
            || salesType.Contains("buy", StringComparison.OrdinalIgnoreCase)
            || salesType.Equals("P", StringComparison.OrdinalIgnoreCase);
        var isSale = salesType.Contains("sale", StringComparison.OrdinalIgnoreCase)
            || salesType.Equals("S", StringComparison.OrdinalIgnoreCase);

        var purchaseAmount = request.PurchaseAmount
            ?? (isPurchase ? request.Amount : 0m);
        var saleAmount = request.SaleAmount
            ?? (isSale ? request.Amount : 0m);
        var qtyPurchase = request.QtyPurchase
            ?? (isPurchase ? request.Qty : 0m);
        var qtySale = request.QtySale
            ?? (isSale ? request.Qty : 0m);

        // When type is ambiguous but Amount/Qty provided, keep explicit overrides only.
        if (!isPurchase && !isSale && request.PurchaseAmount == null && request.SaleAmount == null)
        {
            purchaseAmount = 0m;
            saleAmount = 0m;
        }
        if (!isPurchase && !isSale && request.QtyPurchase == null && request.QtySale == null)
        {
            qtyPurchase = 0m;
            qtySale = 0m;
        }

        row.AccountId = request.AccountId;
        row.Ref = string.IsNullOrWhiteSpace(request.Ref) ? row.Ref : request.Ref.Trim();
        row.PlatformName = TrimOrNull(request.PlatformName, 120);
        row.TransTypeId = request.TransTypeId;
        row.TransTypeName = TrimOrNull(request.TransTypeName, 120);
        row.SalesTypeId = request.SalesTypeId;
        row.SalesTypeName = TrimOrNull(request.SalesTypeName, 120);
        row.Qty = request.Qty;
        row.AmzProRef = TrimOrNull(request.AmzProRef, 120);
        row.TotProCharges = request.TotProCharges;
        row.TotPromotion = request.TotPromotion;
        row.TotProRebate = request.TotProRebate;
        row.AmazonFee = request.AmazonFee;
        row.Descriptions = TrimOrNull(request.Descriptions, 2000);
        row.OrderId = TrimOrNull(request.OrderId, 120);
        row.Other = request.Other;
        row.Amount = request.Amount;
        row.PurchaseAmount = purchaseAmount;
        row.SaleAmount = saleAmount;
        row.QtyPurchase = qtyPurchase;
        row.QtySale = qtySale;
        row.StatusId = request.StatusId;
        row.StatusName = TrimOrNull(request.StatusName, 120);
        row.TransDate = request.TransDate;
        row.DocName = TrimOrNull(request.DocName, 200);
        row.Remarks = TrimOrNull(request.Remarks, 2000);
    }

    private static string? TrimOrNull(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var t = value.Trim();
        return t.Length <= max ? t : t[..max];
    }
}
