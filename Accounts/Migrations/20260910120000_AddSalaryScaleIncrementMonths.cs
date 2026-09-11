using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

/// <summary>
/// Multi Inc Month support: yearly IncrementSal is split across configured calendar months (1–12).
/// Empty list = one full yearly increment (legacy behaviour).
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260910120000_AddSalaryScaleIncrementMonths")]
public sealed class AddSalaryScaleIncrementMonths : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Separate statements: SQL Server validates the whole batch, so ADD must commit before UPDATE.
        migrationBuilder.Sql(
            """
            IF COL_LENGTH('dbo.SalaryScales', 'IncrementMonths') IS NULL
                ALTER TABLE dbo.SalaryScales ADD IncrementMonths nvarchar(100) NULL;
            """);

        migrationBuilder.Sql(
            """
            UPDATE dbo.SalaryScales
            SET IncrementMonths = CAST(IncrementMonth AS nvarchar(10))
            WHERE IncrementMonth IS NOT NULL
              AND IncrementMonth BETWEEN 1 AND 12
              AND (IncrementMonths IS NULL OR LTRIM(RTRIM(IncrementMonths)) = N'');
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF COL_LENGTH('dbo.SalaryScales', 'IncrementMonths') IS NOT NULL
                ALTER TABLE dbo.SalaryScales DROP COLUMN IncrementMonths;
            """);
    }
}
