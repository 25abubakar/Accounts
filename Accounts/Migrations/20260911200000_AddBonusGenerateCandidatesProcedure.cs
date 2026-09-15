using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

/// <summary>
/// Deploys dbo.usp_Pay_BonusGenerate_Candidates (DOJ months vs Min_Service).
/// Keep in sync with Accounts/Sql/StoredProcedures/usp_Pay_BonusGenerate_Candidates.sql
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260911200000_AddBonusGenerateCandidatesProcedure")]
public sealed class AddBonusGenerateCandidatesProcedure : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Applied from the dedicated .sql script in local/dev; this embeds CREATE for other environments.
        migrationBuilder.Sql(@"
CREATE OR ALTER PROCEDURE dbo.usp_Pay_BonusGenerate_Candidates
    @TenantId INT,
    @BenefitRuleId INT,
    @Year INT,
    @Month INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @PeriodEnd date = EOMONTH(DATEFROMPARTS(@Year, @Month, 1));
    DECLARE @RuleScale nvarchar(80), @RuleOrgId int, @RuleMinService decimal(9,2), @RuleServiceStatus nvarchar(80), @RuleIsIneligible bit, @RuleFound bit = 0;
    SELECT @RuleFound = 1, @RuleScale = NULLIF(LTRIM(RTRIM(r.Scale)), N''), @RuleOrgId = r.OrganizationId,
           @RuleMinService = COALESCE(r.MinimumService, 0), @RuleServiceStatus = NULLIF(LTRIM(RTRIM(r.ServiceStatus)), N''),
           @RuleIsIneligible = r.IsIneligible
    FROM dbo.PayrollBenefitRules r
    WHERE r.TenantId = @TenantId AND r.Id = @BenefitRuleId AND r.BenefitsType = N'Bonus';
    IF @RuleFound = 0
    BEGIN
        SELECT CAST(NULL AS uniqueidentifier) AS PersonId WHERE 1 = 0;
        RETURN;
    END;

    ;WITH OrgUp AS (
        SELECT o.Id, o.ParentId, o.Id AS StartId
        FROM dbo.OrganizationTree o
        WHERE @RuleOrgId IS NOT NULL AND o.Id IN (
            SELECT DISTINCT s.OrganizationId FROM dbo.vw_StaffDirectory s
            WHERE s.TenantId = @TenantId AND s.IsPersonActive = 1 AND s.OrganizationId IS NOT NULL)
        UNION ALL
        SELECT p.Id, p.ParentId, c.StartId FROM dbo.OrganizationTree p INNER JOIN OrgUp c ON c.ParentId = p.Id
    ),
    StaffReady AS (
        SELECT s.PersonId, s.StaffId, s.EmployeeId AS EmployeeNumber, s.FullName, s.Designation, s.Department,
               CAST(hr.JoiningDate AS date) AS DateOfJoining, hr.Scale,
               CAST(CASE WHEN hr.PersonId IS NULL THEN 0 ELSE 1 END AS bit) AS HasHrProfile,
               COALESCE(NULLIF(hr.CurrentPay, 0), NULLIF(hr.BasicSalary, 0), 0) AS Salary,
               NULLIF(hr.BasicSalary, 0) AS BasicSalary, NULLIF(hr.CurrentPay, 0) AS CurrentPay,
               p.EmploymentStatus,
               CASE WHEN hr.JoiningDate IS NULL OR CAST(hr.JoiningDate AS date) > @PeriodEnd THEN 0
                    ELSE CASE WHEN DATEDIFF(MONTH, CAST(hr.JoiningDate AS date), @PeriodEnd)
                              - CASE WHEN DAY(@PeriodEnd) < DAY(CAST(hr.JoiningDate AS date)) THEN 1 ELSE 0 END < 0 THEN 0
                         ELSE DATEDIFF(MONTH, CAST(hr.JoiningDate AS date), @PeriodEnd)
                              - CASE WHEN DAY(@PeriodEnd) < DAY(CAST(hr.JoiningDate AS date)) THEN 1 ELSE 0 END END END AS ServiceMonths,
               CAST(CASE WHEN @RuleOrgId IS NULL THEN 1
                         WHEN s.OrganizationId IS NULL THEN 0
                         WHEN EXISTS (SELECT 1 FROM OrgUp u WHERE u.StartId = s.OrganizationId AND u.Id = @RuleOrgId) THEN 1
                         ELSE 0 END AS bit) AS OrgMatch
        FROM dbo.vw_StaffDirectory s
        LEFT JOIN dbo.PersonHrProfiles hr ON hr.PersonId = s.PersonId AND hr.TenantId = s.TenantId
        LEFT JOIN dbo.Persons p ON p.PersonId = s.PersonId
        WHERE s.TenantId = @TenantId AND s.IsPersonActive = 1
    ),
    RankedParams AS (
        SELECT sc.PersonId, p.Id AS ParameterId, p.AmountType, p.PayType, p.Amount, p.Percentage,
               COALESCE(d.BasicPercentage, 0) AS BasicPercentage, COALESCE(d.ServicePercentage, 0) AS ServicePercentage,
               COALESCE(d.ServiceYears, 0) AS DistributionServiceYears, COALESCE(d.AssessmentPercentage, 0) AS AssessmentPercentage,
               COALESCE(d.AttendancePercentage, 0) AS AttendancePercentage, COALESCE(d.LeavePercentage, 0) AS LeavePercentage,
               COALESCE(d.DisciplinePercentage, 0) AS DisciplinePercentage, COALESCE(NULLIF(d.Installments, 0), 1) AS Installments,
               ROW_NUMBER() OVER (PARTITION BY sc.PersonId ORDER BY p.MinimumService DESC, p.Id DESC) AS rn
        FROM StaffReady sc
        INNER JOIN dbo.PayrollBenefitParameters p ON p.BenefitRuleId = @BenefitRuleId AND p.TenantId = @TenantId AND sc.ServiceMonths >= p.MinimumService
        INNER JOIN dbo.PayrollBonusDistributions d ON d.BenefitParameterId = p.Id AND d.TenantId = p.TenantId AND (d.Month IS NULL OR d.Month = @Month)
    ),
    BestParam AS (SELECT * FROM RankedParams WHERE rn = 1)
    SELECT sc.PersonId, sc.StaffId, sc.EmployeeNumber, sc.FullName, sc.Designation, sc.Department, sc.DateOfJoining, sc.Scale,
           sc.ServiceMonths, CAST(FLOOR(sc.ServiceMonths / 12.0) AS decimal(9,2)) AS ServiceYears, sc.Salary AS BaseSalary, bp.ParameterId,
           CAST(CASE WHEN msg.ValidationMessage = N'' THEN 1 ELSE 0 END AS bit) AS IsValid,
           CASE WHEN msg.ValidationMessage = N'' THEN N'Eligible' ELSE msg.ValidationMessage END AS ValidationMessage,
           amounts.BonusAmount,
           CASE WHEN bp.ParameterId IS NULL THEN CAST(100 AS decimal(9,4)) ELSE bp.BasicPercentage END AS BasicPercent,
           CASE WHEN bp.ParameterId IS NULL THEN CAST(0 AS decimal(9,4))
                WHEN bp.DistributionServiceYears > 0 THEN CAST(bp.ServicePercentage * CASE WHEN FLOOR(sc.ServiceMonths / 12.0) / bp.DistributionServiceYears > 1 THEN 1 ELSE FLOOR(sc.ServiceMonths / 12.0) / bp.DistributionServiceYears END AS decimal(9,4))
                ELSE CAST(bp.ServicePercentage AS decimal(9,4)) END AS ServicePercent,
           COALESCE(bp.AttendancePercentage, 0) AS AttendancePercent, COALESCE(bp.AssessmentPercentage, 0) AS AssessmentPercent,
           COALESCE(bp.LeavePercentage, 0) AS LeavePercent, COALESCE(bp.DisciplinePercentage, 0) AS DisciplinePercent,
           COALESCE(bp.Installments, 1) AS Installment
    FROM StaffReady sc
    LEFT JOIN BestParam bp ON bp.PersonId = sc.PersonId
    CROSS APPLY (SELECT CAST(ROUND(CASE WHEN bp.ParameterId IS NULL THEN 0
        WHEN LOWER(LTRIM(RTRIM(bp.AmountType))) = N'figure' THEN COALESCE(bp.Amount, 0)
        ELSE (CASE LOWER(LTRIM(RTRIM(bp.PayType))) WHEN N'basic' THEN COALESCE(sc.BasicSalary, sc.Salary)
             WHEN N'current' THEN COALESCE(sc.CurrentPay, sc.Salary) WHEN N'currentpay' THEN COALESCE(sc.CurrentPay, sc.Salary)
             ELSE sc.Salary END) * COALESCE(bp.Percentage, 0) / 100.0 END, 2) AS decimal(18,2)) AS BonusAmount) amounts
    CROSS APPLY (SELECT COALESCE(STUFF(CONCAT(
        CASE WHEN sc.HasHrProfile = 0 THEN N'; HR profile is missing' ELSE N'' END,
        CASE WHEN sc.HasHrProfile = 1 AND LOWER(LTRIM(RTRIM(COALESCE(bp.AmountType, N'')))) <> N'figure' AND sc.Salary <= 0 THEN N'; Salary is missing' ELSE N'' END,
        CASE WHEN @RuleScale IS NOT NULL AND (sc.Scale IS NULL OR LOWER(LTRIM(RTRIM(sc.Scale))) <> LOWER(@RuleScale)) THEN N'; Requires scale ' + @RuleScale ELSE N'' END,
        CASE WHEN sc.OrgMatch = 0 THEN N'; Organization / entitled scope does not match' ELSE N'' END,
        CASE WHEN sc.DateOfJoining IS NOT NULL AND sc.DateOfJoining > @PeriodEnd THEN N'; Joined after this bonus period' ELSE N'' END,
        CASE WHEN sc.ServiceMonths < @RuleMinService THEN N'; Minimum service is ' + CONVERT(nvarchar(32), CAST(@RuleMinService AS decimal(18,2))) + N' month(s) (DOJ service ' + CONVERT(nvarchar(32), sc.ServiceMonths) + N' month(s))' ELSE N'' END,
        CASE WHEN @RuleIsIneligible = 1 THEN N'; Rule is marked ineligible' ELSE N'' END,
        CASE WHEN @RuleServiceStatus IS NOT NULL AND LOWER(@RuleServiceStatus) NOT IN (N'all', N'active') AND (sc.EmploymentStatus IS NULL OR LOWER(LTRIM(RTRIM(sc.EmploymentStatus))) <> LOWER(@RuleServiceStatus)) THEN N'; Requires service status ' + @RuleServiceStatus ELSE N'' END,
        CASE WHEN bp.ParameterId IS NULL THEN N'; No matching bonus distribution for this month/service' ELSE N'' END,
        CASE WHEN bp.ParameterId IS NOT NULL AND amounts.BonusAmount <= 0 THEN N'; Bonus Figure / Pay Ref amount is not configured' ELSE N'' END
    ), 1, 2, N''), N'') AS ValidationMessage) msg
    ORDER BY sc.FullName
    OPTION (MAXRECURSION 100);
END
");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.usp_Pay_BonusGenerate_Candidates;");
    }
}
