using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261009100000_AddSaleRoznamchaPurchasePrice")]
public sealed class AddSaleRoznamchaPurchasePrice : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "PurchasePrice",
            table: "SaleRoznamchaProducts",
            type: "decimal(18,2)",
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.Sql(SaleRoznamchaSql.InventoryListProcedure);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.usp_SaleRoznamcha_InventoryList;");
        migrationBuilder.DropColumn(
            name: "PurchasePrice",
            table: "SaleRoznamchaProducts");
        migrationBuilder.Sql(PreviousInventoryListProcedure);
    }

    private const string PreviousInventoryListProcedure =
        """
        CREATE OR ALTER PROCEDURE dbo.usp_SaleRoznamcha_InventoryList
            @TenantId int,
            @CategoryId int = NULL,
            @CompanyId int = NULL,
            @PlatformId int = NULL,
            @ProductCategoryId int = NULL,
            @ActiveOnly bit = 0
        AS
        BEGIN
            SET NOCOUNT ON;

            SELECT product.Id,
                   product.Name ProductName,
                   category.Id CategoryId,
                   category.Name CategoryName,
                   company.Id CompanyId,
                   company.Name CompanyName,
                   platform.Id PlatformId,
                   platform.Name PlatformName,
                   productCategory.Id ProductCategoryId,
                   productCategory.Name ProductCategoryName,
                   product.QuantityOnHand,
                   product.IsActive,
                   product.CreatedOnUtc,
                   product.UpdatedOnUtc
            FROM dbo.SaleRoznamchaProducts product
            INNER JOIN dbo.SaleRoznamchaProductCategories productCategory
                ON productCategory.TenantId = product.TenantId
               AND productCategory.Id = product.ProductCategoryId
            INNER JOIN dbo.SaleRoznamchaPlatforms platform
                ON platform.TenantId = productCategory.TenantId
               AND platform.Id = productCategory.PlatformId
            INNER JOIN dbo.SaleRoznamchaCompanies company
                ON company.TenantId = platform.TenantId
               AND company.Id = platform.CompanyId
            INNER JOIN dbo.SaleRoznamchaCategories category
                ON category.TenantId = company.TenantId
               AND category.Id = company.CategoryId
            WHERE product.TenantId = @TenantId
              AND product.IsDeleted = 0
              AND (@CategoryId IS NULL OR category.Id = @CategoryId)
              AND (@CompanyId IS NULL OR company.Id = @CompanyId)
              AND (@PlatformId IS NULL OR platform.Id = @PlatformId)
              AND (@ProductCategoryId IS NULL OR productCategory.Id = @ProductCategoryId)
              AND (@ActiveOnly = 0 OR product.IsActive = 1)
            ORDER BY category.Name, company.Name, platform.Name, productCategory.Name, product.Name;
        END;
        """;
}
