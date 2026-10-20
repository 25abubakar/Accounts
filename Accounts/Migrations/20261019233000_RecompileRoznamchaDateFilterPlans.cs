using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Text.RegularExpressions;

#nullable disable

namespace Accounts.Migrations;

/// <summary>
/// Tenant/date selectivity varies dramatically between a single day and a full
/// legacy ledger. Do not reuse a narrow/other-tenant plan for these list reports.
/// Only procedure definitions change; financial records and indexes are untouched.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20261019233000_RecompileRoznamchaDateFilterPlans")]
public sealed class RecompileRoznamchaDateFilterPlans : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(ForCurrentParameters(RoznamchaListSql.TypedPayment));
        migrationBuilder.Sql(ForCurrentParameters(RoznamchaListSql.TypedReceipt));
        migrationBuilder.Sql(ForCurrentParameters(RoznamchaListSql.TransferUnified));
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(RoznamchaListSql.TypedPayment);
        migrationBuilder.Sql(RoznamchaListSql.TypedReceipt);
        migrationBuilder.Sql(RoznamchaListSql.TransferUnified);
    }

    private static string ForCurrentParameters(string sql)
    {
        var header = new Regex(@"(?m)^AS\r?$", RegexOptions.CultureInvariant);
        if (header.Matches(sql).Count != 1)
            throw new InvalidOperationException("Expected exactly one list procedure header.");
        return header.Replace(sql, "WITH RECOMPILE\nAS", 1);
    }
}
