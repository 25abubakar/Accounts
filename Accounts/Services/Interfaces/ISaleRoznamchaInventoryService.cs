using Accounts.DTOs;

namespace Accounts.Services.Interfaces;

public interface ISaleRoznamchaInventoryService
{
    Task<IReadOnlyList<SaleRoznamchaCurrencyRow>> ListCurrenciesAsync(CancellationToken ct = default);

    Task<IReadOnlyList<SaleRoznamchaInventoryRow>> ListAsync(
        int? categoryId,
        int? companyId,
        int? platformId,
        int? productCategoryId,
        bool activeOnly,
        CancellationToken ct = default);

    Task<SaleRoznamchaInventoryRow> SaveAsync(
        long? id,
        SaveSaleRoznamchaInventoryRequest request,
        CancellationToken ct = default);

    Task<SaleRoznamchaInventoryRow> AddStockAsync(
        long id,
        AddSaleRoznamchaStockRequest request,
        CancellationToken ct = default);

    Task DeleteAsync(long id, CancellationToken ct = default);
}
