using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260717130000_AddAttendanceReportStyleProjection")]
public sealed class AddAttendanceReportStyleProjection : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE OR ALTER PROCEDURE dbo.usp_Attendance_DailyReport
            @TenantId int, @DateFrom date, @DateTo date, @VisiblePersonIds nvarchar(max)
        AS
        BEGIN
            SET NOCOUNT ON;
            ;WITH Dates AS (
                SELECT @DateFrom AttendanceDate UNION ALL
                SELECT DATEADD(day,1,AttendanceDate) FROM Dates WHERE AttendanceDate < @DateTo
            ), VisiblePeople AS (
                SELECT TRY_CONVERT(uniqueidentifier,[value]) PersonId FROM OPENJSON(@VisiblePersonIds)
                WHERE TRY_CONVERT(uniqueidentifier,[value]) IS NOT NULL
            )
            SELECT ar.Id,p.PersonId,COALESCE(sv.LoginId,v.VacancyCode) EmployeeNumber,p.FullName EmployeeName,
                COALESCE(v.Department,org.Name) Department,COALESCE(jt.TitleName,v.JobTitle,N'') Designation,d.AttendanceDate,
                ar.AttendanceStatusId,st.StatusName,ps.Code StatusCode,cs.ColorCode StatusColorCode,
                cs.FontColor StatusFontColor,cs.FontSize StatusFontSize,
                ar.AttendanceEntryTypeId,COALESCE(aet.Name,CASE WHEN ar.Id IS NULL THEN noEntry.Name END) AttendanceEntryType,
                ar.AttendanceWorkModeId,awm.Name AttendanceWorkMode,
                ar.CheckInUtc,ar.CheckOutUtc,ar.TotalBreakMinutes,p.ShiftStartTime,p.ShiftEndTime,p.TimeZoneId,p.ReportsToPersonId
            FROM VisiblePeople vp
            JOIN Persons p ON p.PersonId=vp.PersonId AND p.IsActive=1 AND p.TenantId=@TenantId
            JOIN StaffVacancy sv ON sv.PersonId=p.PersonId JOIN Vacancies v ON v.VacancyId=sv.VacancyId
            LEFT JOIN JobTitles jt ON jt.Id=v.JobTitleId LEFT JOIN OrganizationTree org ON org.Id=v.OrganizationId CROSS JOIN Dates d
            LEFT JOIN AttendanceRecords ar ON ar.PersonId=p.PersonId AND ar.AttendanceDate=d.AttendanceDate AND ar.TenantId=@TenantId
            LEFT JOIN ProcessStatusStyles ps ON ps.Id=ar.AttendanceStatusId LEFT JOIN Statuses st ON st.Id=ps.StatusId
            LEFT JOIN ColorStyles cs ON cs.Id=ps.ColorStyleId
            LEFT JOIN AttendanceEntryTypes aet ON aet.Id=ar.AttendanceEntryTypeId
            LEFT JOIN AttendanceEntryTypes noEntry ON noEntry.Code=N'NONE' AND noEntry.IsActive=1
            LEFT JOIN AttendanceWorkModes awm ON awm.Id=ar.AttendanceWorkModeId
            WHERE DATENAME(weekday,d.AttendanceDate) NOT IN(N'Saturday',N'Sunday') OR ar.Id IS NOT NULL
            ORDER BY d.AttendanceDate DESC,p.FullName OPTION(MAXRECURSION 367);
        END
        """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Attendance reporting style projection is retained.");
}
