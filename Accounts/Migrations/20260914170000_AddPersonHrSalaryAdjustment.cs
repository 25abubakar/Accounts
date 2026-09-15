using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

/// <summary>
/// Manual Salary Adjustment on Staff Accounts → included in payroll gross until edited again.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260914170000_AddPersonHrSalaryAdjustment")]
public sealed class AddPersonHrSalaryAdjustment : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
IF COL_LENGTH(N'dbo.PersonHrProfiles', N'SalaryAdjustment') IS NULL
    ALTER TABLE dbo.PersonHrProfiles ADD SalaryAdjustment decimal(18,2) NULL;

IF COL_LENGTH(N'dbo.PayrollLines', N'SalaryAdjustment') IS NULL
    ALTER TABLE dbo.PayrollLines ADD SalaryAdjustment decimal(18,2) NOT NULL CONSTRAINT DF_PayrollLines_SalaryAdjustment DEFAULT (0);

CREATE OR ALTER VIEW dbo.vw_PersonHrProfiles AS
SELECT
    p.PersonId, p.TenantId, p.FullName, s.LoginId, s.StaffId, v.VacancyCode,
    COALESCE(j.TitleName, v.JobTitle) AS JobTitle, v.Department, p.Phone, p.Email,
    COALESCE(p.PersonalEmail, pcPersonal.ContactValue) AS PersonalEmail,
    p.Gender, p.DateOfBirth, p.MaritalStatus, p.ShiftStartTime, p.ShiftEndTime, p.TimeZoneId,
    h.CnicOrLicense, h.Nationality, h.Race, h.Language, h.BloodGroup, h.Disability, h.PoliceStation,
    COALESCE(h.EmergencyContactNo, pcEmergency.ContactValue) AS EmergencyContactNo,
    h.MedicalFrom, h.MedicalTo, h.Treatment, h.DiagnosisDisease, h.Doctor, h.DoctorContactNo,
    h.BankName, h.BankBranchName, h.BankBranchCode, h.SwiftCode, h.AccountTitle, h.AccountNo, h.IbanNo,
    h.BankBranchContactNo, h.TaxNumber, h.PaymentMode, h.InductionType, h.JoiningDate,
    h.TrainingFrom, h.TrainingTo, h.ProbationFrom, h.ProbationTo, h.ContractFrom, h.ContractTo,
    h.WorkingDays, h.WorkingHours, h.TimingFrom, h.TimingTo, h.PostingPerHour, h.PostingPerDay,
    h.PromotionFrom, h.PromotionTo, h.SalaryPackageId, h.Scale, h.ScaleDate, h.BasicSalary,
    h.IncrementSalary, h.MaxSalary, h.CurrentPay, h.SalaryAdjustment, h.AccountsPerDay, h.AccountsPerHour,
    h.LeaveFrom, h.LeaveTo, h.LeaveEntitled, h.LeaveAvailed
FROM dbo.Persons p
LEFT JOIN dbo.PersonHrProfiles h ON h.PersonId = p.PersonId
LEFT JOIN dbo.StaffVacancy s ON s.PersonId = p.PersonId
LEFT JOIN dbo.Vacancies v ON v.VacancyId = s.VacancyId
LEFT JOIN dbo.JobTitles j ON j.Id = v.JobTitleId
OUTER APPLY (
    SELECT TOP (1) c.ContactValue FROM dbo.PersonContacts c
    WHERE c.PersonId = p.PersonId AND c.ContactType = 'PersonalEmail'
    ORDER BY c.IsPrimary DESC, c.CreatedDate DESC
) pcPersonal
OUTER APPLY (
    SELECT TOP (1) c.ContactValue FROM dbo.PersonContacts c
    WHERE c.PersonId = p.PersonId AND c.ContactType = 'Emergency'
    ORDER BY c.IsPrimary DESC, c.CreatedDate DESC
) pcEmergency;
");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
IF COL_LENGTH(N'dbo.PayrollLines', N'SalaryAdjustment') IS NOT NULL
BEGIN
    DECLARE @df sysname;
    SELECT @df = dc.name
    FROM sys.default_constraints dc
    INNER JOIN sys.columns c ON c.default_object_id = dc.object_id
    WHERE dc.parent_object_id = OBJECT_ID(N'dbo.PayrollLines') AND c.name = N'SalaryAdjustment';
    IF @df IS NOT NULL EXEC(N'ALTER TABLE dbo.PayrollLines DROP CONSTRAINT [' + @df + N']');
    ALTER TABLE dbo.PayrollLines DROP COLUMN SalaryAdjustment;
END

IF COL_LENGTH(N'dbo.PersonHrProfiles', N'SalaryAdjustment') IS NOT NULL
    ALTER TABLE dbo.PersonHrProfiles DROP COLUMN SalaryAdjustment;
");
    }
}
