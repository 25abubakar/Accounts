using Accounts.Models.SpListRows;
using Accounts.DTOs;

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

    Task<IReadOnlyList<RoznamchaUpdateListRow>> ListRoznamchaUpdateAsync(
        int tenantId, DateOnly dateFrom, DateOnly dateTo, int? departmentId = null,
        Guid? staffId = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RozEntryProcessLogDto>> ListRoznamchaUpdateHistoryAsync(
        int tenantId, long entryId, CancellationToken cancellationToken = default);

    Task ProcessRoznamchaUpdateAsync(
        int tenantId, long entryId, ChangeRozProcessLogRequest request, string? userId,
        CancellationToken cancellationToken = default);

    Task UpdateRoznamchaInlineAsync(
        int tenantId, long entryId, UpdateRoznamchaInlineRequest request, string? userId,
        CancellationToken cancellationToken = default);
}
