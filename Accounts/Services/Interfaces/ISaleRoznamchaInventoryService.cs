using Accounts.DTOs;

namespace Accounts.Services.Interfaces;

public interface ISaleRoznamchaInventoryService
{
    Task<IReadOnlyList<SaleRoznamchaCurrencyRow>> ListCurrenciesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<SaleRoznamchaAccountLookupRow>> ListAccountsAsync(CancellationToken ct = default);

    Task<IReadOnlyList<SaleRoznamchaInventoryRow>> ListAsync(
        int? companyId,
        int? platformId,
        int? productCategoryId,
        bool activeOnly,
        CancellationToken ct = default);

    Task<IReadOnlyList<SaleRoznamchaInventoryPurchaseRow>> ListPurchasesAsync(
        int? companyId, int? platformId, int? productCategoryId,
        CancellationToken ct = default);

    Task<SaleRoznamchaInventoryRow> SaveProductAsync(
        long? id,
        SaveSaleRoznamchaProductRequest request,
        CancellationToken ct = default);

    Task<SaleRoznamchaInventoryRow> AddStockAsync(
        long id,
        AddSaleRoznamchaStockRequest request,
        CancellationToken ct = default);

    Task<SaleRoznamchaPurchaseImportResult> ImportPurchasesAsync(
        IFormFile excelFile,
        CancellationToken ct = default);

    Task<string> UploadPurchaseAttachmentAsync(
        long purchaseId,
        IFormFile file,
        CancellationToken ct = default);

    Task<SaleRoznamchaPurchaseAttachmentContent> GetPurchaseAttachmentAsync(
        long purchaseId,
        CancellationToken ct = default);

    Task DeleteProductAsync(long id, CancellationToken ct = default);
}
