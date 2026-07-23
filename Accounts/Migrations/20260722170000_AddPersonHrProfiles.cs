using System;
using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260722170000_AddPersonHrProfiles")]
    public partial class AddPersonHrProfiles : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PersonHrProfiles",
                columns: table => new
                {
                    PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    CnicOrLicense = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Nationality = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    Race = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    Language = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    BloodGroup = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Disability = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    PoliceStation = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    EmergencyContactNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    MedicalFrom = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MedicalTo = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Treatment = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    DiagnosisDisease = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    Doctor = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    DoctorContactNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    BankName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    BankBranchName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    BankBranchCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    SwiftCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    AccountTitle = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    AccountNo = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    IbanNo = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    BankBranchContactNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    TaxNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    PaymentMode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    InductionType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    JoiningDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TrainingFrom = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TrainingTo = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ProbationFrom = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ProbationTo = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ContractFrom = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ContractTo = table.Column<DateTime>(type: "datetime2", nullable: true),
                    WorkingDays = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    WorkingHours = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    TimingFrom = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    TimingTo = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    PostingPerHour = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    PostingPerDay = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    PromotionFrom = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PromotionTo = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Scale = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    ScaleDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    BasicSalary = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    IncrementSalary = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    MaxSalary = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    CurrentPay = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    AccountsPerDay = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    AccountsPerHour = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    LeaveFrom = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LeaveTo = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LeaveEntitled = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    LeaveAvailed = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersonHrProfiles", x => x.PersonId);
                    table.ForeignKey("FK_PersonHrProfiles_Persons_PersonId", x => x.PersonId, "Persons", "PersonId", onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PersonEducations",
                columns: table => new
                {
                    EducationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EducationLevel = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    DegreeTitle = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Institute = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: true),
                    PassingYear = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Grade = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersonEducations", x => x.EducationId);
                    table.ForeignKey("FK_PersonEducations_Persons_PersonId", x => x.PersonId, "Persons", "PersonId", onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PersonExperiences",
                columns: table => new
                {
                    ExperienceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyName = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: true),
                    Role = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EndDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Summary = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersonExperiences", x => x.ExperienceId);
                    table.ForeignKey("FK_PersonExperiences_Persons_PersonId", x => x.PersonId, "Persons", "PersonId", onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex("IX_PersonHrProfiles_TenantId", "PersonHrProfiles", "TenantId");
            migrationBuilder.CreateIndex("IX_PersonEducations_TenantId_PersonId_SortOrder", "PersonEducations", new[] { "TenantId", "PersonId", "SortOrder" });
            migrationBuilder.CreateIndex("IX_PersonEducations_PersonId", "PersonEducations", "PersonId");
            migrationBuilder.CreateIndex("IX_PersonExperiences_TenantId_PersonId_SortOrder", "PersonExperiences", new[] { "TenantId", "PersonId", "SortOrder" });
            migrationBuilder.CreateIndex("IX_PersonExperiences_PersonId", "PersonExperiences", "PersonId");

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
            migrationBuilder.DropTable("PersonExperiences");
            migrationBuilder.DropTable("PersonEducations");
            migrationBuilder.DropTable("PersonHrProfiles");
        }
    }
}
