using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

/// <summary>
/// Payment/Receipt ToCatId is not stored. It is mapped live from ToAccountId
/// through dbo.vw_RoznamchaToAccountCategory so moving an account to another
/// category cannot leave a stale ToCatId on the voucher.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20261015140000_RoznamchaToAccountCategoryView")]
public sealed class RoznamchaToAccountCategoryView : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(ExecutableBatch(SaleRoznamchaSql.AccountHierarchyView));
        migrationBuilder.Sql(ExecutableBatch(SaleRoznamchaSql.ToAccountCategoryView));
        migrationBuilder.Sql(ExecutableBatch(RoznamchaListSql.Payment));
        migrationBuilder.Sql(ExecutableBatch(RoznamchaListSql.Receipt));
        migrationBuilder.Sql(ExecutableBatch(RoznamchaListSql.Unified));
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP VIEW IF EXISTS dbo.vw_RoznamchaToAccountCategory;");
    }

    private static string ExecutableBatch(string sql) => $"EXEC(N'{sql.Replace("'", "''")}');";
}
