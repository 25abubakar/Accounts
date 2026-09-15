using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

/// <summary>
/// Bonus workspace is rule-driven (legacy StaffBonus): period comes from Benefits Distribution,
/// not a separate UI month calendar. Extends dbo.usp_Pay_BonusRules_List.
/// Keep in sync with Accounts/Sql/StoredProcedures/Wave1_PayAttendance_ListProcedures.sql
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260914140000_BonusRulesListPeriodFromDistribution")]
public sealed class BonusRulesListPeriodFromDistribution : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
CREATE OR ALTER PROCEDURE dbo.usp_Pay_BonusRules_List
    @TenantId INT
AS
BEGIN
    SET NOCOUNT ON;
    ;WITH Dist AS
    (
        SELECT
            p.BenefitRuleId,
            d.Month AS DistMonth,
            d.InstallmentStart,
            d.InstallmentEnd,
            d.Installments,
            ROW_NUMBER() OVER (
                PARTITION BY p.BenefitRuleId
                ORDER BY p.MinimumService DESC, p.Id DESC
            ) AS rn
        FROM dbo.PayrollBenefitParameters p
        INNER JOIN dbo.PayrollBonusDistributions d
            ON d.BenefitParameterId = p.Id
           AND d.TenantId = p.TenantId
        WHERE p.TenantId = @TenantId
    )
    SELECT
        r.Id,
        r.BenefitReference AS reference,
        r.Name,
        r.ValidFrom,
        r.ValidTo,
        r.Scale,
        r.Frequency,
        r.MaximumExpense AS maximumExpense,
        r.MinimumService,
        r.MinimumSalary,
        r.OrganizationId,
        r.Company,
        r.Entitled,
        CAST(COALESCE(
            YEAR(d.InstallmentStart),
            YEAR(r.ValidFrom),
            YEAR(SYSUTCDATETIME())
        ) AS int) AS periodYear,
        CAST(COALESCE(
            MONTH(d.InstallmentStart),
            NULLIF(d.DistMonth, 0),
            MONTH(r.ValidFrom),
            1
        ) AS int) AS periodMonth,
        d.InstallmentStart AS installmentStart,
        d.InstallmentEnd AS installmentEnd,
        COALESCE(NULLIF(d.Installments, 0), 1) AS installments
    FROM dbo.PayrollBenefitRules r
    LEFT JOIN Dist d ON d.BenefitRuleId = r.Id AND d.rn = 1
    WHERE r.TenantId = @TenantId
      AND r.BenefitsType = N'Bonus'
      AND r.IsIneligible = 0
    ORDER BY r.ValidFrom DESC, r.Name;
END
");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
CREATE OR ALTER PROCEDURE dbo.usp_Pay_BonusRules_List
    @TenantId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        r.Id,
        r.BenefitReference AS reference,
        r.Name,
        r.ValidFrom,
        r.ValidTo,
        r.Scale,
        r.Frequency,
        r.MaximumExpense AS maximumExpense,
        r.MinimumService,
        r.MinimumSalary,
        r.OrganizationId,
        r.Company,
        r.Entitled
    FROM dbo.PayrollBenefitRules r
    WHERE r.TenantId = @TenantId
      AND r.BenefitsType = N'Bonus'
      AND r.IsIneligible = 0
    ORDER BY r.ValidFrom DESC, r.Name;
END
");
    }
}
