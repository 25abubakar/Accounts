using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

/// <summary>
/// Receipt (ROZ) must not read dbo.PaymentRoznamcha. Inventory purchases remain Payment-typed
/// and stay on Payment (ROZ) only.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20261014230000_UnlinkPaymentRoznamchaFromReceiptRoz")]
public sealed class UnlinkPaymentRoznamchaFromReceiptRoz : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(RoznamchaListSql.Receipt);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(RoznamchaListSql.LegacyReceipt);
}
