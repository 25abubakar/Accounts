using Accounts.Models.SpListRows;

namespace Accounts.Services.Interfaces;

public interface IAccountsRoznamchaService
{
    Task<IReadOnlyList<PaymentRozListRow>> ListPaymentRozAsync(
        int tenantId,
        DateOnly dateFrom,
        DateOnly dateTo,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PaymentRozListRow>> ListReceiptRozAsync(
        int tenantId,
        DateOnly dateFrom,
        DateOnly dateTo,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RoznamchaListRow>> ListRoznamchaAsync(
        int tenantId,
        DateOnly dateFrom,
        DateOnly dateTo,
        int? rozTypeId = null,
        CancellationToken cancellationToken = default);

    Task<int?> ResolveRozTypeIdAsync(int tenantId, string typeCode, CancellationToken cancellationToken = default);
}
