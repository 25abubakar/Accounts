using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260911193000_DropBenefitParameterPeriodDates")]
public sealed class DropBenefitParameterPeriodDates : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF COL_LENGTH(N'dbo.PayrollBenefitParameters', N'PeriodFrom') IS NOT NULL
                ALTER TABLE dbo.PayrollBenefitParameters DROP COLUMN PeriodFrom;
            IF COL_LENGTH(N'dbo.PayrollBenefitParameters', N'PeriodTo') IS NOT NULL
                ALTER TABLE dbo.PayrollBenefitParameters DROP COLUMN PeriodTo;

            EXEC(N'
            CREATE OR ALTER PROCEDURE dbo.usp_Pay_BenefitParameters_List
                @TenantId INT
            AS
            BEGIN
                SET NOCOUNT ON;
                SELECT
                    p.Id,
                    r.Name AS RuleName,
                    p.Name,
                    p.Reference AS [Ref],
                    COALESCE(r.Entitled, r.CompanyName) AS Entitled,
                    p.BenefitRuleId AS BenefitId,
                    r.Frequency AS FreqId,
                    p.MinimumService AS MinSer,
                    p.AmountType AS AmtType,
                    p.PayType AS PayTypeId,
                    p.Amount,
                    p.Percentage,
                    r.MaximumPh AS MaxPh,
                    r.MinimumPh AS MinPh,
                    p.CompanyShare AS CoyShare,
                    p.StaffShare,
                    r.BenefitsType,
                    d.Month AS BonusMonth,
                    COALESCE(d.BasicPercentage, 0) AS BasicPercentage,
                    COALESCE(d.ServicePercentage, 0) AS ServicePercentage,
                    COALESCE(d.ServiceYears, 0) AS ServiceYears,
                    COALESCE(d.AssessmentPercentage, 0) AS AssessmentPercentage,
                    COALESCE(d.AttendancePercentage, 0) AS AttendancePercentage,
                    COALESCE(d.LeavePercentage, 0) AS LeavePercentage,
                    COALESCE(d.DisciplinePercentage, 0) AS DisciplinePercentage,
                    COALESCE(d.Installments, 1) AS Installments
                FROM dbo.PayrollBenefitParameters p
                INNER JOIN dbo.PayrollBenefitRules r ON r.Id = p.BenefitRuleId AND r.TenantId = p.TenantId
                LEFT JOIN dbo.PayrollBonusDistributions d ON d.BenefitParameterId = p.Id AND d.TenantId = p.TenantId
                WHERE p.TenantId = @TenantId
                ORDER BY p.Name;
            END
            ');
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF COL_LENGTH(N'dbo.PayrollBenefitParameters', N'PeriodFrom') IS NULL
                ALTER TABLE dbo.PayrollBenefitParameters ADD PeriodFrom date NULL;
            IF COL_LENGTH(N'dbo.PayrollBenefitParameters', N'PeriodTo') IS NULL
                ALTER TABLE dbo.PayrollBenefitParameters ADD PeriodTo date NULL;
            """);
    }
}
