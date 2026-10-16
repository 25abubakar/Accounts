using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

/// <summary>
/// Makes FromAccountId the single source of truth for the displayed source category.
/// The category is resolved through vw_AccountsChartAccountHierarchy at query time,
/// so moving an account to another category does not leave stale Roznamcha mappings.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20261015123000_ResolveRoznamchaFromCategoryFromAccountView")]
public sealed class ResolveRoznamchaFromCategoryFromAccountView : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(ExecutableBatch(SaleRoznamchaSql.AccountHierarchyView));
        migrationBuilder.Sql(ExecutableBatch(RoznamchaListSql.Payment));
        migrationBuilder.Sql(ExecutableBatch(RoznamchaListSql.Receipt));
        migrationBuilder.Sql(ExecutableBatch(RoznamchaListSql.Unified));
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(ExecutableBatch(UseStoredEntryCategory(RoznamchaListSql.Payment)));
        migrationBuilder.Sql(ExecutableBatch(UseStoredEntryCategory(RoznamchaListSql.Receipt)));
        migrationBuilder.Sql(ExecutableBatch(UseStoredEntryCategory(RoznamchaListSql.Unified)));
    }

    private static string UseStoredEntryCategory(string sql) => sql
        .Replace("fromCat.Name AS CategoryName", "cat.Name AS CategoryName", StringComparison.Ordinal)
        .Replace(
            "LEFT JOIN dbo.vw_AccountsChartAccountHierarchy fromAcct ON fromAcct.Id=e.FromAccountId AND fromAcct.TenantId=e.TenantId\n            LEFT JOIN dbo.AccountsCategories fromCat ON fromCat.Id=fromAcct.CategoryId AND fromCat.TenantId=e.TenantId",
            "LEFT JOIN dbo.AccountsCategories cat ON cat.Id=e.CategoryId AND cat.TenantId=e.TenantId\n            LEFT JOIN dbo.AccountsChartAccounts fromAcct ON fromAcct.Id=e.FromAccountId AND fromAcct.TenantId=e.TenantId",
            StringComparison.Ordinal);

    private static string ExecutableBatch(string sql) => $"EXEC(N'{sql.Replace("'", "''")}');";
}
