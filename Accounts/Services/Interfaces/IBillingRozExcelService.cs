using Accounts.DTOs;
using Microsoft.AspNetCore.Http;

namespace Accounts.Services.Interfaces;

public interface IBillingRozExcelService
{
    Task<int> UploadBillingExcelAsync(int tenantId, IFormFile file, string? userId, CancellationToken ct = default);
    Task<IReadOnlyList<BillingRozExcelRowDto>> ListStagedAsync(int tenantId, DateOnly? dateFrom, DateOnly? dateTo, bool? isSettled = null, CancellationToken ct = default);
    Task<int> SettleStagedIntoRoznamchaAsync(int tenantId, List<int> ids, string? userId, CancellationToken ct = default);
    Task<bool> DeleteStagedAsync(int tenantId, int id, CancellationToken ct = default);
    Task<BillingRozExcelSummaryDto> GetSummaryAsync(int tenantId, CancellationToken ct = default);
}

