using Accounts.Data;
using Accounts.Models.SpListRows;
using Accounts.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Services;

public sealed class AccountsRoznamchaService(ApplicationDbContext db) : IAccountsRoznamchaService
{
    public async Task<IReadOnlyList<PaymentRozListRow>> ListPaymentRozAsync(
        int tenantId,
        DateOnly dateFrom,
        DateOnly dateTo,
        CancellationToken cancellationToken = default) =>
        await SpListQuery.ExecAsync<PaymentRozListRow>(
            db,
            "EXEC dbo.usp_Accounts_PaymentRoz_List @TenantId, @DateFrom, @DateTo",
            cancellationToken,
            SpListQuery.TenantId(tenantId),
            SpListQuery.Date("@DateFrom", dateFrom),
            SpListQuery.Date("@DateTo", dateTo));

    public async Task<IReadOnlyList<PaymentRozListRow>> ListReceiptRozAsync(
        int tenantId,
        DateOnly dateFrom,
        DateOnly dateTo,
        CancellationToken cancellationToken = default) =>
        await SpListQuery.ExecAsync<PaymentRozListRow>(
            db,
            "EXEC dbo.usp_Accounts_ReceiptRoz_List @TenantId, @DateFrom, @DateTo",
            cancellationToken,
            SpListQuery.TenantId(tenantId),
            SpListQuery.Date("@DateFrom", dateFrom),
            SpListQuery.Date("@DateTo", dateTo));

    public async Task<IReadOnlyList<RoznamchaListRow>> ListRoznamchaAsync(
        int tenantId,
        DateOnly dateFrom,
        DateOnly dateTo,
        int? rozTypeId,
        CancellationToken cancellationToken = default) =>
        await SpListQuery.ExecAsync<RoznamchaListRow>(
            db,
            "EXEC dbo.usp_Accounts_Roznamcha_List @TenantId, @RozTypeId, @DateFrom, @DateTo",
            cancellationToken,
            SpListQuery.TenantId(tenantId),
            SpListQuery.IntNullable("@RozTypeId", rozTypeId),
            SpListQuery.Date("@DateFrom", dateFrom),
            SpListQuery.Date("@DateTo", dateTo));

    public async Task<int?> ResolveRozTypeIdAsync(int tenantId, string typeCode, CancellationToken cancellationToken = default)
    {
        var settings = await db.AccountsModuleSettings.AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .Select(x => new { x.PaymentRozTypeId, x.ReceiptRozTypeId })
            .FirstOrDefaultAsync(cancellationToken);

        if (string.Equals(typeCode, "PAYMENT", StringComparison.OrdinalIgnoreCase) && settings?.PaymentRozTypeId is int paymentId)
            return paymentId;
        if (string.Equals(typeCode, "RECEIPT", StringComparison.OrdinalIgnoreCase) && settings?.ReceiptRozTypeId is int receiptId)
            return receiptId;

        return await db.AccountsRoznamchaTypes.AsNoTracking()
            .Where(x => x.IsActive && x.Code == typeCode)
            .OrderByDescending(x => x.TenantId.HasValue)
            .Select(x => (int?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
