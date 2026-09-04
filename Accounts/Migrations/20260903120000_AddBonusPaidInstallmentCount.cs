using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260903120000_AddBonusPaidInstallmentCount")]
public sealed class AddBonusPaidInstallmentCount : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF COL_LENGTH('dbo.PayrollBonusLines', 'PaidInstallmentCount') IS NULL
            BEGIN
                ALTER TABLE dbo.PayrollBonusLines
                ADD PaidInstallmentCount int NOT NULL
                    CONSTRAINT DF_PayrollBonusLines_PaidInstallmentCount DEFAULT (0);
            END
            """);

        migrationBuilder.Sql(
            """
            -- Already fully paid lines: treat all installments as consumed.
            UPDATE dbo.PayrollBonusLines
            SET PaidInstallmentCount = CASE
                    WHEN IsPaid = 1 THEN CASE WHEN Installment < 1 THEN 1 ELSE Installment END
                    ELSE PaidInstallmentCount
                END
            WHERE IsPaid = 1
              AND PaidInstallmentCount = 0;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF COL_LENGTH('dbo.PayrollBonusLines', 'PaidInstallmentCount') IS NOT NULL
            BEGIN
                DECLARE @df sysname;
                SELECT @df = dc.name
                FROM sys.default_constraints dc
                INNER JOIN sys.columns c ON c.default_object_id = dc.object_id
                WHERE dc.parent_object_id = OBJECT_ID(N'dbo.PayrollBonusLines')
                  AND c.name = N'PaidInstallmentCount';
                IF @df IS NOT NULL EXEC(N'ALTER TABLE dbo.PayrollBonusLines DROP CONSTRAINT [' + @df + N']');
                ALTER TABLE dbo.PayrollBonusLines DROP COLUMN PaidInstallmentCount;
            END
            """);
    }
}
