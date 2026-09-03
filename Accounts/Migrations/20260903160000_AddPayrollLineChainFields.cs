using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

/// <summary>
/// Payroll line fields for Staff Payroll chain display:
/// scale basics, allowance splits (Pay Scale), taxable income, pending attendance flag.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260903160000_AddPayrollLineChainFields")]
public sealed class AddPayrollLineChainFields : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF COL_LENGTH('dbo.PayrollLines', 'ScaleDate') IS NULL
                ALTER TABLE dbo.PayrollLines ADD ScaleDate date NULL;
            IF COL_LENGTH('dbo.PayrollLines', 'ContractType') IS NULL
                ALTER TABLE dbo.PayrollLines ADD ContractType nvarchar(50) NULL;
            IF COL_LENGTH('dbo.PayrollLines', 'Month') IS NULL
                ALTER TABLE dbo.PayrollLines ADD [Month] int NOT NULL CONSTRAINT DF_PayrollLines_Month DEFAULT (0);
            IF COL_LENGTH('dbo.PayrollLines', 'Year') IS NULL
                ALTER TABLE dbo.PayrollLines ADD [Year] int NOT NULL CONSTRAINT DF_PayrollLines_Year DEFAULT (0);
            IF COL_LENGTH('dbo.PayrollLines', 'ScaleBasicSalary') IS NULL
                ALTER TABLE dbo.PayrollLines ADD ScaleBasicSalary decimal(18,2) NOT NULL CONSTRAINT DF_PayrollLines_ScaleBasicSalary DEFAULT (0);
            IF COL_LENGTH('dbo.PayrollLines', 'IncrementSalary') IS NULL
                ALTER TABLE dbo.PayrollLines ADD IncrementSalary decimal(18,2) NOT NULL CONSTRAINT DF_PayrollLines_IncrementSalary DEFAULT (0);
            IF COL_LENGTH('dbo.PayrollLines', 'MaxSalary') IS NULL
                ALTER TABLE dbo.PayrollLines ADD MaxSalary decimal(18,2) NOT NULL CONSTRAINT DF_PayrollLines_MaxSalary DEFAULT (0);
            IF COL_LENGTH('dbo.PayrollLines', 'CurrentPay') IS NULL
                ALTER TABLE dbo.PayrollLines ADD CurrentPay decimal(18,2) NOT NULL CONSTRAINT DF_PayrollLines_CurrentPay DEFAULT (0);
            IF COL_LENGTH('dbo.PayrollLines', 'GeneralAllowanceAmount') IS NULL
                ALTER TABLE dbo.PayrollLines ADD GeneralAllowanceAmount decimal(18,2) NOT NULL CONSTRAINT DF_PayrollLines_GeneralAllowanceAmount DEFAULT (0);
            IF COL_LENGTH('dbo.PayrollLines', 'ApptAllowanceAmount') IS NULL
                ALTER TABLE dbo.PayrollLines ADD ApptAllowanceAmount decimal(18,2) NOT NULL CONSTRAINT DF_PayrollLines_ApptAllowanceAmount DEFAULT (0);
            IF COL_LENGTH('dbo.PayrollLines', 'ShiftAllowanceAmount') IS NULL
                ALTER TABLE dbo.PayrollLines ADD ShiftAllowanceAmount decimal(18,2) NOT NULL CONSTRAINT DF_PayrollLines_ShiftAllowanceAmount DEFAULT (0);
            IF COL_LENGTH('dbo.PayrollLines', 'TaxableIncome') IS NULL
                ALTER TABLE dbo.PayrollLines ADD TaxableIncome decimal(18,2) NOT NULL CONSTRAINT DF_PayrollLines_TaxableIncome DEFAULT (0);
            IF COL_LENGTH('dbo.PayrollLines', 'IsPending') IS NULL
                ALTER TABLE dbo.PayrollLines ADD IsPending bit NOT NULL CONSTRAINT DF_PayrollLines_IsPending DEFAULT (0);
            IF COL_LENGTH('dbo.PayrollLines', 'PendingReviewDays') IS NULL
                ALTER TABLE dbo.PayrollLines ADD PendingReviewDays int NOT NULL CONSTRAINT DF_PayrollLines_PendingReviewDays DEFAULT (0);

            -- Backfill Month/Year from run when still defaulted.
            UPDATE pl
            SET pl.[Month] = pr.[Month], pl.[Year] = pr.[Year]
            FROM dbo.PayrollLines pl
            INNER JOIN dbo.PayrollRuns pr ON pr.Id = pl.PayrollRunId
            WHERE pl.[Month] = 0 OR pl.[Year] = 0;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE dbo.PayrollLines DROP COLUMN IF EXISTS ScaleDate;
            ALTER TABLE dbo.PayrollLines DROP COLUMN IF EXISTS ContractType;
            IF COL_LENGTH('dbo.PayrollLines', 'Month') IS NOT NULL
            BEGIN
                ALTER TABLE dbo.PayrollLines DROP CONSTRAINT IF EXISTS DF_PayrollLines_Month;
                ALTER TABLE dbo.PayrollLines DROP COLUMN [Month];
            END
            IF COL_LENGTH('dbo.PayrollLines', 'Year') IS NOT NULL
            BEGIN
                ALTER TABLE dbo.PayrollLines DROP CONSTRAINT IF EXISTS DF_PayrollLines_Year;
                ALTER TABLE dbo.PayrollLines DROP COLUMN [Year];
            END
            ALTER TABLE dbo.PayrollLines DROP COLUMN IF EXISTS ScaleBasicSalary;
            ALTER TABLE dbo.PayrollLines DROP COLUMN IF EXISTS IncrementSalary;
            ALTER TABLE dbo.PayrollLines DROP COLUMN IF EXISTS MaxSalary;
            ALTER TABLE dbo.PayrollLines DROP COLUMN IF EXISTS CurrentPay;
            ALTER TABLE dbo.PayrollLines DROP COLUMN IF EXISTS GeneralAllowanceAmount;
            ALTER TABLE dbo.PayrollLines DROP COLUMN IF EXISTS ApptAllowanceAmount;
            ALTER TABLE dbo.PayrollLines DROP COLUMN IF EXISTS ShiftAllowanceAmount;
            ALTER TABLE dbo.PayrollLines DROP COLUMN IF EXISTS TaxableIncome;
            ALTER TABLE dbo.PayrollLines DROP COLUMN IF EXISTS IsPending;
            ALTER TABLE dbo.PayrollLines DROP COLUMN IF EXISTS PendingReviewDays;
            """);
    }
}
