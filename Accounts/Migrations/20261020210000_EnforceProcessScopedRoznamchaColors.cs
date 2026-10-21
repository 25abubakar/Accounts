using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261020210000_EnforceProcessScopedRoznamchaColors")]
public sealed class EnforceProcessScopedRoznamchaColors : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Restore the process + status lookup; never inherit another process's style.
        // No entry status, amount, mapping, or other business data is updated.
        migrationBuilder.Sql(((SqlOperation)new UnifyPaymentReceiptRozList().UpOperations[0]).Sql);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.Sql(((SqlOperation)new UseConfiguredReceiptStatusColors().UpOperations[0]).Sql);
}
