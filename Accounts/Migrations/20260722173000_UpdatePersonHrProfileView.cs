using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260722173000_UpdatePersonHrProfileView")]
    public partial class UpdatePersonHrProfileView : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
CREATE OR ALTER VIEW dbo.vw_PersonHrProfiles AS
SELECT
    p.PersonId,
    p.TenantId,
    p.FullName,
    s.LoginId,
    s.StaffId,
    v.VacancyCode,
    COALESCE(j.TitleName, v.JobTitle) AS JobTitle,
    v.Department,
    p.Phone,
    p.Email,
    COALESCE(p.PersonalEmail, pcPersonal.ContactValue) AS PersonalEmail,
    p.Gender,
    p.DateOfBirth,
    p.MaritalStatus,
    p.ShiftStartTime,
    p.ShiftEndTime,
    p.TimeZoneId,
    h.CnicOrLicense,
    h.Nationality,
    h.Race,
    h.Language,
    h.BloodGroup,
    h.Disability,
    h.PoliceStation,
    COALESCE(h.EmergencyContactNo, pcEmergency.ContactValue) AS EmergencyContactNo,
    h.MedicalFrom,
    h.MedicalTo,
    h.Treatment,
    h.DiagnosisDisease,
    h.Doctor,
    h.DoctorContactNo,
    h.BankName,
    h.BankBranchName,
    h.BankBranchCode,
    h.SwiftCode,
    h.AccountTitle,
    h.AccountNo,
    h.IbanNo,
    h.BankBranchContactNo,
    h.TaxNumber,
    h.PaymentMode,
    h.InductionType,
    h.JoiningDate,
    h.TrainingFrom,
    h.TrainingTo,
    h.ProbationFrom,
    h.ProbationTo,
    h.ContractFrom,
    h.ContractTo,
    h.WorkingDays,
    h.WorkingHours,
    h.TimingFrom,
    h.TimingTo,
    h.PostingPerHour,
    h.PostingPerDay,
    h.PromotionFrom,
    h.PromotionTo,
    h.Scale,
    h.ScaleDate,
    h.BasicSalary,
    h.IncrementSalary,
    h.MaxSalary,
    h.CurrentPay,
    h.AccountsPerDay,
    h.AccountsPerHour,
    h.LeaveFrom,
    h.LeaveTo,
    h.LeaveEntitled,
    h.LeaveAvailed
FROM dbo.Persons p
LEFT JOIN dbo.PersonHrProfiles h ON h.PersonId = p.PersonId
LEFT JOIN dbo.StaffVacancy s ON s.PersonId = p.PersonId
LEFT JOIN dbo.Vacancies v ON v.VacancyId = s.VacancyId
LEFT JOIN dbo.JobTitles j ON j.Id = v.JobTitleId
OUTER APPLY (
    SELECT TOP (1) c.ContactValue
    FROM dbo.PersonContacts c
    WHERE c.PersonId = p.PersonId AND c.ContactType = 'PersonalEmail'
    ORDER BY c.IsPrimary DESC, c.CreatedDate DESC
) pcPersonal
OUTER APPLY (
    SELECT TOP (1) c.ContactValue
    FROM dbo.PersonContacts c
    WHERE c.PersonId = p.PersonId AND c.ContactType = 'Emergency'
    ORDER BY c.IsPrimary DESC, c.CreatedDate DESC
) pcEmergency;
""");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW IF EXISTS dbo.vw_PersonHrProfiles;");
        }
    }
}
