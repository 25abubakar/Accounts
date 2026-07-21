using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260721141000_AddStaffDirectoryPerformanceView")]
public sealed class AddStaffDirectoryPerformanceView : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE OR ALTER VIEW dbo.vw_StaffDirectory
            AS
            SELECT
                staff.TenantId,
                staff.StaffId,
                staff.PersonId,
                COALESCE(staff.LoginId, vacancy.VacancyCode, N'') AS EmployeeId,
                person.FullName,
                COALESCE(vacancy.Department, organizationNode.Name, N'') AS Department,
                COALESCE(jobTitle.TitleName, vacancy.JobTitle, N'') AS Designation,
                person.ProfilePhotoUrl AS PhotoUrl,
                person.ReportsToPersonId,
                person.ShiftStartTime,
                person.ShiftEndTime,
                person.TimeZoneId,
                vacancy.OrganizationId,
                person.IsActive AS IsPersonActive
            FROM dbo.StaffVacancy AS staff
            INNER JOIN dbo.Persons AS person
                ON person.PersonId = staff.PersonId
               AND person.TenantId = staff.TenantId
            INNER JOIN dbo.Vacancies AS vacancy
                ON vacancy.VacancyId = staff.VacancyId
               AND vacancy.TenantId = staff.TenantId
            LEFT JOIN dbo.JobTitles AS jobTitle
                ON jobTitle.Id = vacancy.JobTitleId
            LEFT JOIN dbo.OrganizationTree AS organizationNode
                ON organizationNode.Id = vacancy.OrganizationId;
            """);

        migrationBuilder.Sql(
            """
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AttendanceRecords_TenantId_PersonId_AttendanceDate' AND object_id = OBJECT_ID(N'dbo.AttendanceRecords'))
                CREATE INDEX IX_AttendanceRecords_TenantId_PersonId_AttendanceDate
                ON dbo.AttendanceRecords(TenantId, PersonId, AttendanceDate)
                INCLUDE (AttendanceStatusId, AttendanceEntryTypeId, AttendanceWorkModeId, CheckInUtc, CheckOutUtc, TotalBreakMinutes);

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AttendanceRecords_TenantId_AttendanceDate_PersonId' AND object_id = OBJECT_ID(N'dbo.AttendanceRecords'))
                CREATE INDEX IX_AttendanceRecords_TenantId_AttendanceDate_PersonId
                ON dbo.AttendanceRecords(TenantId, AttendanceDate, PersonId)
                INCLUDE (AttendanceStatusId, AttendanceEntryTypeId, AttendanceWorkModeId, CheckInUtc, CheckOutUtc, TotalBreakMinutes);

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_StaffVacancy_TenantId_PersonId_StaffId' AND object_id = OBJECT_ID(N'dbo.StaffVacancy'))
                CREATE INDEX IX_StaffVacancy_TenantId_PersonId_StaffId
                ON dbo.StaffVacancy(TenantId, PersonId, StaffId)
                INCLUDE (VacancyId, LoginId);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP VIEW IF EXISTS dbo.vw_StaffDirectory;");
    }
}
