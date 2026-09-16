using Accounts.Data;
using Accounts.Models.SpListRows;
using Accounts.Services.Interfaces;

namespace Accounts.Services.Services;

public sealed class AccountsRoznamchaService(ApplicationDbContext db) : IAccountsRoznamchaService
{
    public async Task<IReadOnlyList<PaymentRozListRow>> ListPaymentRozAsync(
        int tenantId,
        DateOnly dateFrom,
        DateOnly dateTo,
        CancellationToken cancellationToken = default)
    {
        var rows = await SpListQuery.ExecAsync<PaymentRozListRow>(
            db,
            "EXEC dbo.usp_Accounts_PaymentRoz_List @TenantId, @DateFrom, @DateTo",
            cancellationToken,
            SpListQuery.TenantId(tenantId),
            SpListQuery.Date("@DateFrom", dateFrom),
            SpListQuery.Date("@DateTo", dateTo));

        return rows;
    }
}
