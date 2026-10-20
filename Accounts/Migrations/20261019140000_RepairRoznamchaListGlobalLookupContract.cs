using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

/// <summary>Reapply the complete list contract after account type lookups became global.</summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20261019140000_RepairRoznamchaListGlobalLookupContract")]
public sealed class RepairRoznamchaListGlobalLookupContract : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(RoznamchaListSql.Payment);
        migrationBuilder.Sql(RoznamchaListSql.Receipt);
        migrationBuilder.Sql(RoznamchaListSql.Unified);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Shared lookup columns no longer exist, so retain the compatible list contract.
        migrationBuilder.Sql(RoznamchaListSql.Payment);
        migrationBuilder.Sql(RoznamchaListSql.Receipt);
        migrationBuilder.Sql(RoznamchaListSql.Unified);
    }
}
