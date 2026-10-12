using Accounts.Data;
using Accounts.DTOs;
using Accounts.Services.Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

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
            var rows = await SpListQuery.ExecAsync<SaleRoznamchaHistoryAggregateRow>(
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

            if (HasProductTrendContract(rows))
                return rows;

            // Rolling-deployment compatibility: an older database procedure grouped trend rows
            // only by date, causing dates to appear as chart series. Keep the report usable until
            // the product-trend migration is applied, without trusting or relabelling aggregate data.
            var productTrends = await LoadProductTrendsAsync(
                fromDate, toDate, categoryId, companyId, platformId, productCategoryId, ct);

            return rows
                .Where(row => row.Dimension is not "TREND_DAY" and not "TREND_MONTH")
                .Concat(productTrends)
                .ToArray();
        }
        catch (SqlException ex) when (ex.Number == 51000)
        {
            throw new InvalidOperationException(ex.Message, ex);
        }
    }

    private static bool HasProductTrendContract(IReadOnlyList<SaleRoznamchaHistoryAggregateRow> rows)
    {
        var productKeys = rows
            .Where(row => row.Dimension == "PRODUCT")
            .Select(row => row.GroupKey)
            .ToHashSet(StringComparer.Ordinal);
        var trendRows = rows
            .Where(row => row.Dimension is "TREND_DAY" or "TREND_MONTH")
            .ToArray();

        return trendRows.Length == 0 ||
               (productKeys.Count > 0 && trendRows.All(row => productKeys.Contains(row.GroupKey)));
    }

    private async Task<IReadOnlyList<SaleRoznamchaHistoryAggregateRow>> LoadProductTrendsAsync(
        DateOnly? fromDate,
        DateOnly? toDate,
        int? categoryId,
        int? companyId,
        int? platformId,
        int? productCategoryId,
        CancellationToken ct)
    {
        var tenantId = current.TenantId;
        var query = db.SaleRoznamchaDailySales
            .AsNoTracking()
            .Where(sale => sale.TenantId == tenantId);

        if (fromDate.HasValue)
        {
            var fromUtc = fromDate.Value.ToDateTime(TimeOnly.MinValue).AddMinutes(-300);
            query = query.Where(sale => sale.CreatedOnUtc >= fromUtc);
        }

        if (toDate.HasValue)
        {
            var toUtcExclusive = toDate.Value.AddDays(1).ToDateTime(TimeOnly.MinValue).AddMinutes(-300);
            query = query.Where(sale => sale.CreatedOnUtc < toUtcExclusive);
        }

        if (categoryId.HasValue)
            query = query.Where(sale => sale.Product.ProductCategory.Platform.Company.CategoryId == categoryId.Value);
        if (companyId.HasValue)
            query = query.Where(sale => sale.Product.ProductCategory.Platform.CompanyId == companyId.Value);
        if (platformId.HasValue)
            query = query.Where(sale => sale.Product.ProductCategory.PlatformId == platformId.Value);
        if (productCategoryId.HasValue)
            query = query.Where(sale => sale.Product.ProductCategoryId == productCategoryId.Value);

        var daily = await query
            .GroupBy(sale => new
            {
                LocalDate = sale.CreatedOnUtc.AddMinutes(300).Date,
                sale.ProductId,
                sale.Product.ProductCode,
                ProductName = sale.Product.Name
            })
            .Select(group => new
            {
                group.Key.LocalDate,
                group.Key.ProductId,
                group.Key.ProductCode,
                group.Key.ProductName,
                TransactionCount = group.LongCount(),
                Quantity = group.Sum(sale => sale.Quantity),
                InventoryDelta = group.Sum(sale => sale.InventoryDelta)
            })
            .ToListAsync(ct);

        var monthly = await query
            .GroupBy(sale => new
            {
                LocalYear = sale.CreatedOnUtc.AddMinutes(300).Year,
                LocalMonth = sale.CreatedOnUtc.AddMinutes(300).Month,
                sale.ProductId,
                sale.Product.ProductCode,
                ProductName = sale.Product.Name
            })
            .Select(group => new
            {
                group.Key.LocalYear,
                group.Key.LocalMonth,
                group.Key.ProductId,
                group.Key.ProductCode,
                group.Key.ProductName,
                TransactionCount = group.LongCount(),
                Quantity = group.Sum(sale => sale.Quantity),
                InventoryDelta = group.Sum(sale => sale.InventoryDelta)
            })
            .ToListAsync(ct);

        var result = new List<SaleRoznamchaHistoryAggregateRow>(daily.Count + monthly.Count);
        result.AddRange(daily.Select(row => new SaleRoznamchaHistoryAggregateRow
        {
            Dimension = "TREND_DAY",
            GroupKey = row.ProductId.ToString(),
            Label = ProductLabel(row.ProductCode, row.ProductName),
            PeriodStart = row.LocalDate,
            SortOrder = row.LocalDate.Year * 10000L + row.LocalDate.Month * 100L + row.LocalDate.Day,
            TransactionCount = row.TransactionCount,
            Quantity = row.Quantity,
            InventoryDelta = row.InventoryDelta,
            ProductCount = 1
        }));
        result.AddRange(monthly.Select(row => new SaleRoznamchaHistoryAggregateRow
        {
            Dimension = "TREND_MONTH",
            GroupKey = row.ProductId.ToString(),
            Label = ProductLabel(row.ProductCode, row.ProductName),
            PeriodStart = new DateTime(row.LocalYear, row.LocalMonth, 1),
            SortOrder = row.LocalYear * 100L + row.LocalMonth,
            TransactionCount = row.TransactionCount,
            Quantity = row.Quantity,
            InventoryDelta = row.InventoryDelta,
            ProductCount = 1
        }));
        return result;
    }

    private static string ProductLabel(string productCode, string productName) =>
        string.IsNullOrWhiteSpace(productCode) ? productName : $"{productCode} - {productName}";
}
