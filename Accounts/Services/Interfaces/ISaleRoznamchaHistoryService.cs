using Accounts.DTOs;

namespace Accounts.Services.Interfaces;

public interface ISaleRoznamchaHistoryService
{
    Task<IReadOnlyList<SaleRoznamchaHistoryAggregateRow>> ReportAsync(
        DateOnly? fromDate,
        DateOnly? toDate,
        int? categoryId,
        int? companyId,
        int? platformId,
        int? productCategoryId,
        CancellationToken ct = default);
}
