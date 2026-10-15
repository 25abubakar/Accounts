using Accounts.DTOs;

namespace Accounts.Services.Interfaces;

public interface ISaleRoznamchaDailySaleService
{
    Task<IReadOnlyList<SaleRoznamchaDailySaleRow>> ListAsync(
        int? companyId,
        int? platformId,
        int? productCategoryId,
        CancellationToken ct = default);

    Task<IReadOnlyList<SaleRoznamchaSaleStatusRow>> StatusesAsync(CancellationToken ct = default);

    Task<SaleRoznamchaDailySaleLookups> LookupsAsync(CancellationToken ct = default);

    Task<SaleRoznamchaDailySaleRow> CreateAsync(
        CreateSaleRoznamchaDailySaleRequest request,
        CancellationToken ct = default);

    Task<IReadOnlyList<SaleRoznamchaInvoiceRow>> ListInvoicesAsync(
        int? companyId, int? platformId, int? productCategoryId,
        CancellationToken ct = default);
    Task<SaleRoznamchaInvoiceRow> GetInvoiceAsync(long id, CancellationToken ct = default);
    Task<SaleRoznamchaInvoiceRow> CreateInvoiceAsync(SaveSaleRoznamchaInvoiceRequest request, CancellationToken ct = default);
    Task<SaleRoznamchaInvoiceRow> UpdateInvoiceAsync(long id, UpdateSaleRoznamchaInvoiceRequest request, CancellationToken ct = default);
}
