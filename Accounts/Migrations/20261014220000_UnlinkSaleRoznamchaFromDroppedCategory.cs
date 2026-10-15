using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261014220000_UnlinkSaleRoznamchaFromDroppedCategory")]
public sealed class UnlinkSaleRoznamchaFromDroppedCategory : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
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
            "Sale Roznamcha inventory cannot be relinked to the dropped Category table automatically.");
    }
}
