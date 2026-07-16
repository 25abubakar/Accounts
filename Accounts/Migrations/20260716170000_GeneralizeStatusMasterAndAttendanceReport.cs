using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260716170000_GeneralizeStatusMasterAndAttendanceReport")]
public sealed class GeneralizeStatusMasterAndAttendanceReport : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Rename in place: AttendanceRecords keeps every existing StatusId/FK value.
        migrationBuilder.RenameTable(name: "AttendanceStatusMaster", newName: "StatusMaster");
        migrationBuilder.RenameIndex(name: "IX_AttendanceStatusMaster_Code", table: "StatusMaster", newName: "IX_StatusMaster_Code_Legacy");
        migrationBuilder.RenameIndex(name: "IX_AttendanceStatusMaster_StatusName", table: "StatusMaster", newName: "IX_StatusMaster_StatusName_Legacy");

        migrationBuilder.AddColumn<string>(
            name: "StatusType", table: "StatusMaster", type: "nvarchar(50)",
            maxLength: 50, nullable: false, defaultValue: "Attendance");

        migrationBuilder.DropIndex(name: "IX_StatusMaster_Code_Legacy", table: "StatusMaster");
        migrationBuilder.DropIndex(name: "IX_StatusMaster_StatusName_Legacy", table: "StatusMaster");
        migrationBuilder.CreateIndex(name: "IX_StatusMaster_StatusType_Code", table: "StatusMaster", columns: new[] { "StatusType", "Code" }, unique: true);
        migrationBuilder.CreateIndex(name: "IX_StatusMaster_StatusType_StatusName", table: "StatusMaster", columns: new[] { "StatusType", "StatusName" }, unique: true);

        // This procedure performs the expensive date x employee x attendance join
        // once in SQL. @VisiblePersonIds is a JSON array produced by the separately
        // authorized organization-hierarchy resolver.
        migrationBuilder.Sql(
            """
            CREATE OR ALTER PROCEDURE dbo.usp_Attendance_DailyReport
                @TenantId int,
                @DateFrom date,
                @DateTo date,
                @VisiblePersonIds nvarchar(max)
            AS
            BEGIN
                SET NOCOUNT ON;

                ;WITH Dates AS
                (
                    SELECT @DateFrom AS AttendanceDate
                    UNION ALL
                    SELECT DATEADD(day, 1, AttendanceDate)
                    FROM Dates
                    WHERE AttendanceDate < @DateTo
                ), VisiblePeople AS
                (
                    SELECT TRY_CONVERT(uniqueidentifier, [value]) AS PersonId
                    FROM OPENJSON(@VisiblePersonIds)
                    WHERE TRY_CONVERT(uniqueidentifier, [value]) IS NOT NULL
                )
                SELECT
                    ar.Id,
                    p.PersonId,
                    COALESCE(sv.LoginId, v.VacancyCode) AS EmployeeNumber,
                    p.FullName AS EmployeeName,
                    COALESCE(v.Department, org.Name) AS Department,
                    COALESCE(jt.TitleName, v.JobTitle, N'') AS Designation,
                    d.AttendanceDate,
                    ar.AttendanceStatusId,
                    sm.StatusName,
                    sm.Code AS StatusCode,
                    sm.ColorCode AS StatusColorCode,
                    ar.CheckInUtc,
                    ar.CheckOutUtc,
                    ar.TotalBreakMinutes,
                    p.ShiftStartTime,
                    p.ShiftEndTime,
                    p.TimeZoneId,
                    p.ReportsToPersonId
                FROM VisiblePeople vp
                INNER JOIN Persons p ON p.PersonId = vp.PersonId AND p.IsActive = 1 AND p.TenantId = @TenantId
                INNER JOIN StaffVacancy sv ON sv.PersonId = p.PersonId
                INNER JOIN Vacancies v ON v.VacancyId = sv.VacancyId
                LEFT JOIN JobTitles jt ON jt.Id = v.JobTitleId
                LEFT JOIN OrganizationTree org ON org.Id = v.OrganizationId
                CROSS JOIN Dates d
                LEFT JOIN AttendanceRecords ar ON ar.PersonId = p.PersonId AND ar.AttendanceDate = d.AttendanceDate AND ar.TenantId = @TenantId
                LEFT JOIN StatusMaster sm ON sm.Id = ar.AttendanceStatusId
                WHERE DATENAME(weekday, d.AttendanceDate) NOT IN (N'Saturday', N'Sunday') OR ar.Id IS NOT NULL
                ORDER BY d.AttendanceDate DESC, p.FullName
                OPTION (MAXRECURSION 367);
            END
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.usp_Attendance_DailyReport;");
        migrationBuilder.DropIndex(name: "IX_StatusMaster_StatusType_Code", table: "StatusMaster");
        migrationBuilder.DropIndex(name: "IX_StatusMaster_StatusType_StatusName", table: "StatusMaster");
        migrationBuilder.DropColumn(name: "StatusType", table: "StatusMaster");
        migrationBuilder.CreateIndex(name: "IX_AttendanceStatusMaster_Code", table: "StatusMaster", column: "Code", unique: true);
        migrationBuilder.CreateIndex(name: "IX_AttendanceStatusMaster_StatusName", table: "StatusMaster", column: "StatusName", unique: true);
        migrationBuilder.RenameTable(name: "StatusMaster", newName: "AttendanceStatusMaster");
    }
}
