using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Text.RegularExpressions;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261019234000_MaterializeRoznamchaAccountLookups")]
public sealed class MaterializeRoznamchaAccountLookups : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(Optimize(RoznamchaListSql.TypedPayment));
        migrationBuilder.Sql(Optimize(RoznamchaListSql.TypedReceipt));
        migrationBuilder.Sql(Optimize(RoznamchaListSql.TransferUnified));
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(Recompile(RoznamchaListSql.TypedPayment));
        migrationBuilder.Sql(Recompile(RoznamchaListSql.TypedReceipt));
        migrationBuilder.Sql(Recompile(RoznamchaListSql.TransferUnified));
    }

    private static string Recompile(string sql) => Regex.Replace(sql, @"(?m)^AS\r?$", "WITH RECOMPILE\nAS");

    private static string Optimize(string sql)
    {
        // Recursive views must be evaluated once per tenant, not once per ledger
        // row in a nested-loop plan. Table variables are request-local and keep
        // SQL Server's first-result-set metadata available to import diagnostics.
        sql = Recompile(sql)
            .Replace("dbo.vw_AccountsChartAccountHierarchy fromAcct", "@RozAccountHierarchy fromAcct", StringComparison.Ordinal)
            .Replace("dbo.vw_RoznamchaToAccountCategory toMap", "@RozToAccountCategory toMap", StringComparison.Ordinal);
        return sql.Replace("SET NOCOUNT ON;", """
            SET NOCOUNT ON;
            DECLARE @RozAccountHierarchy TABLE(Id int NOT NULL,TenantId int NOT NULL,CategoryId int NULL,
                AccountName nvarchar(200) NOT NULL,AccountNumber nvarchar(50) NOT NULL,PRIMARY KEY(TenantId,Id));
            INSERT @RozAccountHierarchy
            SELECT Id,TenantId,CategoryId,AccountName,AccountNumber
            FROM dbo.vw_AccountsChartAccountHierarchy WHERE TenantId=@TenantId;
            DECLARE @RozToAccountCategory TABLE(ToAccountId int NOT NULL,TenantId int NOT NULL,ToCategoryId int NULL,
                ToCategoryName nvarchar(120) NULL,ToAccountName nvarchar(200) NOT NULL,ToAccountNumber nvarchar(50) NOT NULL,
                PRIMARY KEY(TenantId,ToAccountId));
            INSERT @RozToAccountCategory
            SELECT ToAccountId,TenantId,ToCategoryId,ToCategoryName,ToAccountName,ToAccountNumber
            FROM dbo.vw_RoznamchaToAccountCategory WHERE TenantId=@TenantId;
            """, StringComparison.Ordinal);
    }
}
