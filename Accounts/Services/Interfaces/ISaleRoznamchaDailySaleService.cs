using Accounts.DTOs;

namespace Accounts.Services.Interfaces;

public interface ISaleRoznamchaDailySaleService
{
    Task<IReadOnlyList<SaleRoznamchaDailySaleRow>> ListAsync(
        int? categoryId,
        int? companyId,
        int? platformId,
        int? productCategoryId,
        CancellationToken ct = default);

    Task<IReadOnlyList<SaleRoznamchaSaleStatusRow>> StatusesAsync(CancellationToken ct = default);

    Task<SaleRoznamchaDailySaleRow> CreateAsync(
        CreateSaleRoznamchaDailySaleRequest request,
        CancellationToken ct = default);
}
