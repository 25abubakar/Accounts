using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261104201000_ExpandBillingPaymentRemark")]
public sealed class ExpandBillingPaymentRemark : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("ALTER TABLE dbo.tblClaimCodeAmountDetails ALTER COLUMN Remark nvarchar(1000) NULL;");

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("ALTER TABLE dbo.tblClaimCodeAmountDetails ALTER COLUMN Remark nvarchar(500) NULL;");
}
