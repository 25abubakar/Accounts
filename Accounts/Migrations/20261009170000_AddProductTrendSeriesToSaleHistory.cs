using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261009170000_AddProductTrendSeriesToSaleHistory")]
public sealed class AddProductTrendSeriesToSaleHistory : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(ExecutableBatch(SaleRoznamchaSql.HistoryAggregateProcedure));
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(ExecutableBatch(LegacyHistoryAggregateProcedure));
    }

    private const string LegacyHistoryAggregateProcedure =
        """
        CREATE OR ALTER PROCEDURE dbo.usp_SaleRoznamcha_HistoryAggregate
            @TenantId int,
            @FromDate date = NULL,
            @ToDate date = NULL,
            @CategoryId int = NULL,
            @CompanyId int = NULL,
            @PlatformId int = NULL,
            @ProductCategoryId int = NULL
        AS
        BEGIN
            SET NOCOUNT ON;

            IF @FromDate IS NOT NULL AND @ToDate IS NOT NULL AND @FromDate > @ToDate
                THROW 51000, 'From date cannot be after To date.', 1;

            ;WITH Base AS
            (
                SELECT sale.Id, sale.ProductId, sale.Quantity, sale.InventoryDelta,
                       CONVERT(date, DATEADD(minute, 300, sale.CreatedOnUtc)) LocalSaleDate,
                       category.Id CategoryId, category.Name CategoryName,
                       company.Id CompanyId, company.Name CompanyName,
                       platform.Id PlatformId, platform.Name PlatformName,
                       productCategory.Id ProductCategoryId, productCategory.Name ProductCategoryName,
                       product.Name ProductName,
                       status.Id StatusId, status.Name StatusName
                FROM dbo.SaleRoznamchaDailySales sale
                INNER JOIN dbo.SaleRoznamchaProducts product
                    ON product.TenantId = sale.TenantId AND product.Id = sale.ProductId
                INNER JOIN dbo.SaleRoznamchaProductCategories productCategory
                    ON productCategory.TenantId = product.TenantId AND productCategory.Id = product.ProductCategoryId
                INNER JOIN dbo.SaleRoznamchaPlatforms platform
                    ON platform.TenantId = productCategory.TenantId AND platform.Id = productCategory.PlatformId
                INNER JOIN dbo.SaleRoznamchaCompanies company
                    ON company.TenantId = platform.TenantId AND company.Id = platform.CompanyId
                INNER JOIN dbo.SaleRoznamchaCategories category
                    ON category.TenantId = company.TenantId AND category.Id = company.CategoryId
                INNER JOIN dbo.SaleRoznamchaSaleStatuses status
                    ON status.TenantId = sale.TenantId AND status.Id = sale.SaleStatusId
                WHERE sale.TenantId = @TenantId
                  AND (@FromDate IS NULL OR sale.CreatedOnUtc >= DATEADD(minute, -300, CONVERT(datetime2, @FromDate)))
                  AND (@ToDate IS NULL OR sale.CreatedOnUtc < DATEADD(minute, -300, DATEADD(day, 1, CONVERT(datetime2, @ToDate))))
                  AND (@CategoryId IS NULL OR category.Id = @CategoryId)
                  AND (@CompanyId IS NULL OR company.Id = @CompanyId)
                  AND (@PlatformId IS NULL OR platform.Id = @PlatformId)
                  AND (@ProductCategoryId IS NULL OR productCategory.Id = @ProductCategoryId)
            )
            SELECT CAST(N'SUMMARY' AS nvarchar(30)) Dimension,
                   CAST(N'TOTAL' AS nvarchar(200)) GroupKey,
                   CAST(N'Total' AS nvarchar(200)) Label,
                   CAST(NULL AS datetime2) PeriodStart,
                   CAST(0 AS bigint) SortOrder,
                   COUNT_BIG(*) TransactionCount,
                   COALESCE(SUM(Quantity), 0) Quantity,
                   COALESCE(SUM(InventoryDelta), 0) InventoryDelta,
                   COUNT(DISTINCT ProductId) ProductCount
            FROM Base

            UNION ALL
            SELECT N'TREND_DAY', CONVERT(nvarchar(200), LocalSaleDate, 23),
                   CONVERT(nvarchar(200), LocalSaleDate, 23), CONVERT(datetime2, LocalSaleDate),
                   CONVERT(bigint, CONVERT(char(8), LocalSaleDate, 112)),
                   COUNT_BIG(*), SUM(Quantity), SUM(InventoryDelta), COUNT(DISTINCT ProductId)
            FROM Base GROUP BY LocalSaleDate

            UNION ALL
            SELECT N'TREND_MONTH', CONVERT(nvarchar(200), DATEFROMPARTS(YEAR(LocalSaleDate), MONTH(LocalSaleDate), 1), 23),
                   LEFT(DATENAME(month, LocalSaleDate), 3) + N' ' + CONVERT(nvarchar(4), YEAR(LocalSaleDate)),
                   CONVERT(datetime2, DATEFROMPARTS(YEAR(LocalSaleDate), MONTH(LocalSaleDate), 1)),
                   CONVERT(bigint, YEAR(LocalSaleDate) * 100 + MONTH(LocalSaleDate)),
                   COUNT_BIG(*), SUM(Quantity), SUM(InventoryDelta), COUNT(DISTINCT ProductId)
            FROM Base GROUP BY YEAR(LocalSaleDate), MONTH(LocalSaleDate), DATENAME(month, LocalSaleDate)

            UNION ALL
            SELECT N'CATEGORY', CONVERT(nvarchar(200), CategoryId), CategoryName, NULL, CategoryId,
                   COUNT_BIG(*), SUM(Quantity), SUM(InventoryDelta), COUNT(DISTINCT ProductId)
            FROM Base GROUP BY CategoryId, CategoryName

            UNION ALL
            SELECT N'COMPANY', CONVERT(nvarchar(200), CompanyId), CompanyName, NULL, CompanyId,
                   COUNT_BIG(*), SUM(Quantity), SUM(InventoryDelta), COUNT(DISTINCT ProductId)
            FROM Base GROUP BY CompanyId, CompanyName

            UNION ALL
            SELECT N'PLATFORM', CONVERT(nvarchar(200), PlatformId), PlatformName, NULL, PlatformId,
                   COUNT_BIG(*), SUM(Quantity), SUM(InventoryDelta), COUNT(DISTINCT ProductId)
            FROM Base GROUP BY PlatformId, PlatformName

            UNION ALL
            SELECT N'PRODUCT_CATEGORY', CONVERT(nvarchar(200), ProductCategoryId), ProductCategoryName, NULL, ProductCategoryId,
                   COUNT_BIG(*), SUM(Quantity), SUM(InventoryDelta), COUNT(DISTINCT ProductId)
            FROM Base GROUP BY ProductCategoryId, ProductCategoryName

            UNION ALL
            SELECT N'PRODUCT', CONVERT(nvarchar(200), ProductId), ProductName, NULL, ProductId,
                   COUNT_BIG(*), SUM(Quantity), SUM(InventoryDelta), 1
            FROM Base GROUP BY ProductId, ProductName

            UNION ALL
            SELECT N'STATUS', CONVERT(nvarchar(200), StatusId), StatusName, NULL, StatusId,
                   COUNT_BIG(*), SUM(Quantity), SUM(InventoryDelta), COUNT(DISTINCT ProductId)
            FROM Base GROUP BY StatusId, StatusName

            ORDER BY Dimension, SortOrder, Quantity DESC;
        END;
        """;

    private static string ExecutableBatch(string sql) => $"EXEC(N'{sql.Replace("'", "''")}');";
}
