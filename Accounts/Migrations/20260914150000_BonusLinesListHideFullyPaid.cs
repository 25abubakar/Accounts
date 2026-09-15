using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

/// <summary>
/// Legacy StaffBonus: fully paid bonus lines (1 installment paid, or all installments done)
/// drop off the active Bonus grid. Keep in sync with Wave1_PayAttendance_ListProcedures.sql
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260914150000_BonusLinesListHideFullyPaid")]
public sealed class BonusLinesListHideFullyPaid : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
CREATE OR ALTER PROCEDURE dbo.usp_Pay_BonusLines_List
    @TenantId INT,
    @BenefitRuleId INT,
    @Year INT,
    @Month INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        l.Id, l.TenantId, l.BonusRunId, l.PersonId, l.StaffId, l.EmployeeNumber, l.FullName,
        l.Designation, l.Department, l.DateOfJoining, l.Scale, l.IsValid, l.ValidationMessage,
        l.BaseSalary, l.BonusAmount, l.BasicBonus, l.AttendanceBonus, l.LeaveBonus, l.DisciplineBonus,
        l.AssessmentBonus, l.ServiceBonus, l.ServiceYears, l.Month, l.Year, l.TotalBonus,
        l.BasicPercent, l.ServicePercent, l.AttendancePercent, l.AssessmentPercent, l.LeavePercent,
        l.DisciplinePercent, l.InstallmentAmount, l.Installment, l.CurrentInstallmentNo, l.PaidInstallmentCount,
        l.IsApproved, l.IsPaid, l.PaidOnUtc, l.IsInactive, l.Remarks, l.CreatedOnUtc, l.UpdatedOnUtc
    FROM dbo.PayrollBonusLines l
    INNER JOIN dbo.PayrollBonusRuns r ON r.Id = l.BonusRunId AND r.TenantId = l.TenantId
    WHERE l.TenantId = @TenantId
      AND r.BenefitRuleId = @BenefitRuleId
      AND r.Year = @Year
      AND r.Month = @Month
      AND l.IsInactive = 0
      AND l.IsPaid = 0
    ORDER BY l.FullName;
END
");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
CREATE OR ALTER PROCEDURE dbo.usp_Pay_BonusLines_List
    @TenantId INT,
    @BenefitRuleId INT,
    @Year INT,
    @Month INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        l.Id, l.TenantId, l.BonusRunId, l.PersonId, l.StaffId, l.EmployeeNumber, l.FullName,
        l.Designation, l.Department, l.DateOfJoining, l.Scale, l.IsValid, l.ValidationMessage,
        l.BaseSalary, l.BonusAmount, l.BasicBonus, l.AttendanceBonus, l.LeaveBonus, l.DisciplineBonus,
        l.AssessmentBonus, l.ServiceBonus, l.ServiceYears, l.Month, l.Year, l.TotalBonus,
        l.BasicPercent, l.ServicePercent, l.AttendancePercent, l.AssessmentPercent, l.LeavePercent,
        l.DisciplinePercent, l.InstallmentAmount, l.Installment, l.CurrentInstallmentNo, l.PaidInstallmentCount,
        l.IsApproved, l.IsPaid, l.PaidOnUtc, l.IsInactive, l.Remarks, l.CreatedOnUtc, l.UpdatedOnUtc
    FROM dbo.PayrollBonusLines l
    INNER JOIN dbo.PayrollBonusRuns r ON r.Id = l.BonusRunId AND r.TenantId = l.TenantId
    WHERE l.TenantId = @TenantId
      AND r.BenefitRuleId = @BenefitRuleId
      AND r.Year = @Year
      AND r.Month = @Month
      AND l.IsInactive = 0
    ORDER BY l.FullName;
END
");
    }
}
