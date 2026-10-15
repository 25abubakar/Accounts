using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

/// <summary>
/// Keeps inventory purchase batch data and exposes the table as PaymentRoznamcha.
/// Purchase entry UI lives on Payment (ROZ); this only renames the store and refreshes SPs.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20261014200000_RenameStockReceiptsToPaymentRoznamcha")]
public sealed class RenameStockReceiptsToPaymentRoznamcha : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.RenameTable(
            name: "SaleRoznamchaStockReceipts",
            newName: "PaymentRoznamcha");

        RedeployDependentSql(migrationBuilder);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.RenameTable(
            name: "PaymentRoznamcha",
            newName: "SaleRoznamchaStockReceipts");

        RedeployDependentSql(migrationBuilder, useLegacyTableName: true);
    }

    private static void RedeployDependentSql(MigrationBuilder migrationBuilder, bool useLegacyTableName = false)
    {
        string Sql(string value) => useLegacyTableName
            ? value.Replace("dbo.PaymentRoznamcha", "dbo.SaleRoznamchaStockReceipts", StringComparison.Ordinal)
            : value;

        migrationBuilder.Sql(Sql(SaleRoznamchaSql.PostInventoryPurchasePaymentProcedure));
        migrationBuilder.Sql(Sql(SaleRoznamchaSql.InventoryStockAddProcedure));
        migrationBuilder.Sql(Sql(SaleRoznamchaSql.InventoryListProcedure));
        migrationBuilder.Sql(Sql(SaleRoznamchaSql.InventoryPurchaseDetailsView));
        migrationBuilder.Sql(Sql(SaleRoznamchaSql.InventoryPurchaseListProcedure));
        migrationBuilder.Sql(Sql(SaleRoznamchaSql.DailySaleCreateProcedure));
        migrationBuilder.Sql(Sql(RoznamchaListSql.Payment));
        migrationBuilder.Sql(Sql(RoznamchaListSql.Receipt));
        migrationBuilder.Sql(Sql(RoznamchaListSql.Unified));
    }
}
