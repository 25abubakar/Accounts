using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261009110000_AddSaleRoznamchaInventoryCurrency")]
public sealed class AddSaleRoznamchaInventoryCurrency : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "CurrencyId",
            table: "SaleRoznamchaProducts",
            type: "int",
            nullable: true);

        migrationBuilder.Sql(
            """
            UPDATE product
            SET CurrencyId = selectedCurrency.Id
            FROM dbo.SaleRoznamchaProducts product
            LEFT JOIN dbo.AccountsModuleSettings settings
                ON settings.TenantId = product.TenantId
            CROSS APPLY (
                SELECT TOP (1) currency.Id
                FROM dbo.AccountsCurrencies currency
                WHERE currency.IsActive = 1
                  AND (currency.TenantId = product.TenantId OR currency.TenantId IS NULL)
                ORDER BY CASE WHEN currency.Id = settings.DefaultCurrencyId THEN 0
                              WHEN currency.TenantId = product.TenantId THEN 1
                              ELSE 2 END,
                         currency.Code,
                         currency.Id
            ) selectedCurrency;

            IF EXISTS (SELECT 1 FROM dbo.SaleRoznamchaProducts WHERE CurrencyId IS NULL)
                THROW 51000, 'An active currency must exist before Sale Roznamcha inventory currency can be assigned.', 1;
            """);

        migrationBuilder.AlterColumn<int>(
            name: "CurrencyId",
            table: "SaleRoznamchaProducts",
            type: "int",
            nullable: false,
            oldClrType: typeof(int),
            oldType: "int",
            oldNullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_SaleRoznamchaProducts_CurrencyId",
            table: "SaleRoznamchaProducts",
            column: "CurrencyId");

        migrationBuilder.AddForeignKey(
            name: "FK_SaleRoznamchaProducts_AccountsCurrencies_CurrencyId",
            table: "SaleRoznamchaProducts",
            column: "CurrencyId",
            principalTable: "AccountsCurrencies",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.Sql(SaleRoznamchaSql.InventoryListProcedure);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.usp_SaleRoznamcha_InventoryList;");

        migrationBuilder.DropForeignKey(
            name: "FK_SaleRoznamchaProducts_AccountsCurrencies_CurrencyId",
            table: "SaleRoznamchaProducts");

        migrationBuilder.DropIndex(
            name: "IX_SaleRoznamchaProducts_CurrencyId",
            table: "SaleRoznamchaProducts");

        migrationBuilder.DropColumn(
            name: "CurrencyId",
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
                   product.PurchasePrice,
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
