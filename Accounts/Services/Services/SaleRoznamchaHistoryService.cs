using Accounts.Data;
using Accounts.DTOs;
using Accounts.Services.Interfaces;
using Microsoft.Data.SqlClient;

namespace Accounts.Services.Services;

public sealed class SaleRoznamchaHistoryService(
    ApplicationDbContext db,
    ICurrentUserService current) : ISaleRoznamchaHistoryService
{
    public async Task<IReadOnlyList<SaleRoznamchaHistoryAggregateRow>> ReportAsync(
        DateOnly? fromDate,
        DateOnly? toDate,
        int? categoryId,
        int? companyId,
        int? platformId,
        int? productCategoryId,
        CancellationToken ct = default)
    {
        if (fromDate.HasValue && toDate.HasValue && fromDate > toDate)
            throw new InvalidOperationException("From date cannot be after To date.");

        try
        {
            return await SpListQuery.ExecAsync<SaleRoznamchaHistoryAggregateRow>(
                db,
                "EXEC dbo.usp_SaleRoznamcha_HistoryAggregate @TenantId, @FromDate, @ToDate, @CategoryId, @CompanyId, @PlatformId, @ProductCategoryId",
                ct,
                SpListQuery.TenantId(current.TenantId),
                SpListQuery.DateNullable("@FromDate", fromDate),
                SpListQuery.DateNullable("@ToDate", toDate),
                SpListQuery.IntNullable("@CategoryId", categoryId),
                SpListQuery.IntNullable("@CompanyId", companyId),
                SpListQuery.IntNullable("@PlatformId", platformId),
                SpListQuery.IntNullable("@ProductCategoryId", productCategoryId));
        }
        catch (SqlException ex) when (ex.Number == 51000)
        {
            throw new InvalidOperationException(ex.Message, ex);
        }
    }
}
