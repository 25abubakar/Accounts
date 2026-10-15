using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261014210000_RenameManagementToEMarketingAndDropCategory")]
public sealed class RenameManagementToEMarketingAndDropCategory : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            SET NOCOUNT ON;

            UPDATE dbo.Menus
            SET Title = N'E-Marketing'
            WHERE Route = N'/sale-roznamcha/management';

            UPDATE feature
            SET FeatureName = REPLACE(feature.FeatureName, N'Management', N'E-Marketing'),
                Description = REPLACE(feature.Description, N'Management', N'E-Marketing')
            FROM dbo.Features feature
            INNER JOIN dbo.Menus menu
                ON feature.FeatureKey LIKE CONCAT(N'MENU_', menu.Id, N'%')
            WHERE menu.Route = N'/sale-roznamcha/management';

            ;WITH Duplicates AS
            (
                SELECT Id,
                       ROW_NUMBER() OVER (PARTITION BY TenantId, Name ORDER BY Id) AS DuplicateNumber
                FROM dbo.SaleRoznamchaCompanies
            )
            UPDATE company
            SET Name = LEFT(CONCAT(company.Name, N'-', duplicates.DuplicateNumber), 160)
            FROM dbo.SaleRoznamchaCompanies company
            INNER JOIN Duplicates duplicates ON duplicates.Id = company.Id
            WHERE duplicates.DuplicateNumber > 1;
            """);

        migrationBuilder.DropForeignKey(
            name: "FK_SaleRoznamchaCompanies_SaleRoznamchaCategories_TenantId_CategoryId",
            table: "SaleRoznamchaCompanies");

        migrationBuilder.DropIndex(
            name: "IX_SaleRoznamchaCompanies_TenantId_CategoryId_Name",
            table: "SaleRoznamchaCompanies");

        migrationBuilder.DropColumn(
            name: "CategoryId",
            table: "SaleRoznamchaCompanies");

        migrationBuilder.DropTable(
            name: "SaleRoznamchaCategories");

        migrationBuilder.CreateIndex(
            name: "IX_SaleRoznamchaCompanies_TenantId_Name",
            table: "SaleRoznamchaCompanies",
            columns: new[] { "TenantId", "Name" },
            unique: true);

        migrationBuilder.Sql(SaleRoznamchaSql.MasterHierarchyProcedure);
        migrationBuilder.Sql(SaleRoznamchaSql.InventoryListProcedure);
        migrationBuilder.Sql(SaleRoznamchaSql.InventoryPurchaseDetailsView);
        migrationBuilder.Sql(SaleRoznamchaSql.InventoryPurchaseListProcedure);
        migrationBuilder.Sql(SaleRoznamchaSql.DailySaleListProcedure);
        migrationBuilder.Sql(SaleRoznamchaSql.HistoryAggregateProcedure);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        throw new InvalidOperationException(
            "Sale Roznamcha category cannot be restored automatically. Restore from backup if required.");
    }
}
