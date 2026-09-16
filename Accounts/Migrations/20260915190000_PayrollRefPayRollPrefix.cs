using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

/// <summary>
/// Normalize payroll Ref/RunNumber to PayRoll-{month}-{year} on Draft runs.
/// Procedure body source of truth: Sql/StoredProcedures/usp_Payroll_GenerateMonthly.sql
/// (apply CREATE OR ALTER from that file when deploying).
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260915190000_PayrollRefPayRollPrefix")]
public sealed class PayrollRefPayRollPrefix : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE dbo.PayrollRuns
            SET RunNumber = N'PayRoll-' + CAST([Month] AS nvarchar(2)) + N'-' + CAST([Year] AS nvarchar(4)),
                UpdatedOnUtc = SYSUTCDATETIME()
            WHERE UPPER(Status) = N'DRAFT'
              AND (
                    RunNumber IS NULL
                 OR RunNumber LIKE N'PAY-%'
                 OR RunNumber NOT LIKE N'PayRoll-%'
              );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // No-op — PayRoll-{month}-{year} remains the desired format.
    }
}
