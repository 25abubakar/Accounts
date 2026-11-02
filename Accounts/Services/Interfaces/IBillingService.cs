using Accounts.DTOs;
using Accounts.Models;

namespace Accounts.Services.Interfaces;

public interface IBillingService
{
    Task<IReadOnlyList<BillingReceiptRozListRow>> ListBillingReceiptRozAsync(int tenantId, DateOnly? dateFrom, DateOnly? dateTo, CancellationToken ct = default);
    Task<int> SettleReceiptRozAsync(int tenantId, BillingSettleRequest request, string? userId, CancellationToken ct = default);
    Task<int> UnsettleReceiptRozAsync(int tenantId, List<long> ids, string? userId, CancellationToken ct = default);
    Task<bool> UpdateStatusAsync(int tenantId, long id, int statusId, string? remarks, string? userId, CancellationToken ct = default);
    Task<bool> TransferAccountAsync(int tenantId, long id, int transferAccountId, string? userId, CancellationToken ct = default);
    Task<bool> AddProcessLogAsync(int tenantId, BillingProcessLogRequest request, string? userId, CancellationToken ct = default);
}

