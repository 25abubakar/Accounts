using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

/// <summary>
/// Payment (ROZ) can pass an existing journal id into inventory stock-add so
/// product qty/price update without posting a second payment voucher.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20261014240000_PaymentRozProductInventoryStockAdd")]
public sealed class PaymentRozProductInventoryStockAdd : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(SaleRoznamchaSql.InventoryStockAddProcedure);

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
