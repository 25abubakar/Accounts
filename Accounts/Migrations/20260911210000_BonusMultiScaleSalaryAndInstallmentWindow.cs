using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260911210000_BonusMultiScaleSalaryAndInstallmentWindow")]
public sealed class BonusMultiScaleSalaryAndInstallmentWindow : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF COL_LENGTH(N'dbo.PayrollBenefitRules', N'Scale') IS NOT NULL
               AND EXISTS (
                   SELECT 1 FROM sys.columns
                   WHERE object_id = OBJECT_ID(N'dbo.PayrollBenefitRules') AND name = N'Scale' AND max_length = 100
               )
                ALTER TABLE dbo.PayrollBenefitRules ALTER COLUMN Scale nvarchar(500) NULL;

            IF COL_LENGTH(N'dbo.PayrollBenefitRules', N'Scale') IS NOT NULL
            BEGIN
                DECLARE @scaleLen int = (
                    SELECT max_length FROM sys.columns
                    WHERE object_id = OBJECT_ID(N'dbo.PayrollBenefitRules') AND name = N'Scale');
                IF @scaleLen IS NOT NULL AND @scaleLen < 1000
                    ALTER TABLE dbo.PayrollBenefitRules ALTER COLUMN Scale nvarchar(500) NULL;
            END

            IF COL_LENGTH(N'dbo.PayrollBenefitRules', N'MinimumSalary') IS NULL
                ALTER TABLE dbo.PayrollBenefitRules ADD MinimumSalary decimal(18,2) NOT NULL CONSTRAINT DF_PayrollBenefitRules_MinimumSalary DEFAULT (0);

            IF COL_LENGTH(N'dbo.PayrollBonusDistributions', N'InstallmentStart') IS NULL
                ALTER TABLE dbo.PayrollBonusDistributions ADD InstallmentStart date NULL;
            IF COL_LENGTH(N'dbo.PayrollBonusDistributions', N'InstallmentEnd') IS NULL
                ALTER TABLE dbo.PayrollBonusDistributions ADD InstallmentEnd date NULL;

            IF COL_LENGTH(N'dbo.PayrollBonusLines', N'CurrentInstallmentNo') IS NULL
                ALTER TABLE dbo.PayrollBonusLines ADD CurrentInstallmentNo int NOT NULL CONSTRAINT DF_PayrollBonusLines_CurrentInstallmentNo DEFAULT (0);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF COL_LENGTH(N'dbo.PayrollBonusLines', N'CurrentInstallmentNo') IS NOT NULL
            BEGIN
                DECLARE @df1 sysname = (
                    SELECT dc.name FROM sys.default_constraints dc
                    INNER JOIN sys.columns c ON c.default_object_id = dc.object_id
                    WHERE dc.parent_object_id = OBJECT_ID(N'dbo.PayrollBonusLines') AND c.name = N'CurrentInstallmentNo');
                IF @df1 IS NOT NULL EXEC(N'ALTER TABLE dbo.PayrollBonusLines DROP CONSTRAINT [' + @df1 + N']');
                ALTER TABLE dbo.PayrollBonusLines DROP COLUMN CurrentInstallmentNo;
            END

            IF COL_LENGTH(N'dbo.PayrollBonusDistributions', N'InstallmentEnd') IS NOT NULL
                ALTER TABLE dbo.PayrollBonusDistributions DROP COLUMN InstallmentEnd;
            IF COL_LENGTH(N'dbo.PayrollBonusDistributions', N'InstallmentStart') IS NOT NULL
                ALTER TABLE dbo.PayrollBonusDistributions DROP COLUMN InstallmentStart;

            IF COL_LENGTH(N'dbo.PayrollBenefitRules', N'MinimumSalary') IS NOT NULL
            BEGIN
                DECLARE @df2 sysname = (
                    SELECT dc.name FROM sys.default_constraints dc
                    INNER JOIN sys.columns c ON c.default_object_id = dc.object_id
                    WHERE dc.parent_object_id = OBJECT_ID(N'dbo.PayrollBenefitRules') AND c.name = N'MinimumSalary');
                IF @df2 IS NOT NULL EXEC(N'ALTER TABLE dbo.PayrollBenefitRules DROP CONSTRAINT [' + @df2 + N']');
                ALTER TABLE dbo.PayrollBenefitRules DROP COLUMN MinimumSalary;
            END
            """);
    }
}
