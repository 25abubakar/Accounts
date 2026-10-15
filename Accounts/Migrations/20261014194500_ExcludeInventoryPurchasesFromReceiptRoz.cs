using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

/// <summary>
/// Inventory purchases post only to Payment (ROZ). Receipt (ROZ) must never list them,
/// even when ReceiptRozTypeId is unset and would otherwise return every journal row.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20261014194500_ExcludeInventoryPurchasesFromReceiptRoz")]
public sealed class ExcludeInventoryPurchasesFromReceiptRoz : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(RoznamchaListSql.Receipt);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(RoznamchaListSql.LegacyReceipt);
}
