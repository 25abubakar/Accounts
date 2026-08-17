using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    public partial class AddCameraAttendanceVerificationTimes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF COL_LENGTH(N'dbo.AttendanceRecords', N'CameraCheckInUtc') IS NULL
                    ALTER TABLE dbo.AttendanceRecords ADD CameraCheckInUtc datetime2 NULL;

                IF COL_LENGTH(N'dbo.AttendanceRecords', N'CameraCheckOutUtc') IS NULL
                    ALTER TABLE dbo.AttendanceRecords ADD CameraCheckOutUtc datetime2 NULL;
                """);

            migrationBuilder.Sql(
                """
                CREATE OR ALTER PROCEDURE dbo.usp_Attendance_DailyReport
                    @TenantId int, @DateFrom date, @DateTo date, @VisiblePersonIds nvarchar(max)
                AS
                BEGIN
                    SET NOCOUNT ON;
                    DECLARE @ProcessId int,@DayOff int,@Holiday int;
                    SELECT @ProcessId=Id FROM dbo.Processes WHERE ProcessName=N'Attendance';
                    SELECT TOP(1) @DayOff=Id FROM dbo.ProcessStatusStyles
                        WHERE ProcessId=@ProcessId AND Code=N'DO' AND IsActive=1 AND (TenantId=@TenantId OR TenantId IS NULL)
                        ORDER BY CASE WHEN TenantId=@TenantId THEN 0 ELSE 1 END;
                    SELECT TOP(1) @Holiday=Id FROM dbo.ProcessStatusStyles
                        WHERE ProcessId=@ProcessId AND Code=N'H' AND IsActive=1 AND (TenantId=@TenantId OR TenantId IS NULL)
                        ORDER BY CASE WHEN TenantId=@TenantId THEN 0 ELSE 1 END;

                    ;WITH Dates AS(
                        SELECT @DateFrom AttendanceDate UNION ALL
                        SELECT DATEADD(day,1,AttendanceDate) FROM Dates WHERE AttendanceDate<@DateTo
                    ), VisiblePeople AS(
                        SELECT TRY_CONVERT(uniqueidentifier,[value]) PersonId FROM OPENJSON(@VisiblePersonIds)
                        WHERE TRY_CONVERT(uniqueidentifier,[value]) IS NOT NULL
                    ), ReportRows AS(
                        SELECT attendance.Id,person.PersonId,
                            COALESCE(staff.LoginId,vacancy.VacancyCode) EmployeeNumber,
                            person.FullName EmployeeName,
                            COALESCE(vacancy.Department,organization.Name) Department,
                            COALESCE(jobTitle.TitleName,vacancy.JobTitle,N'') Designation,
                            dates.AttendanceDate,
                            COALESCE(attendance.AttendanceStatusId,
                                CASE WHEN effective.IsOn=0 THEN
                                    CASE WHEN timingHoliday.ValueCode IN(N'HOLIDAY',N'ANNUAL_HOLIDAY')
                                         THEN COALESCE(@Holiday,@DayOff)
                                         ELSE COALESCE(@DayOff,@Holiday) END END) AttendanceStatusId,
                            COALESCE(attendance.AttendanceEntryTypeId,mapRule.AttendanceEntryTypeId) AttendanceEntryTypeId,
                            attendance.AttendanceWorkModeId,
                            attendance.CheckInUtc,attendance.CheckOutUtc,
                            attendance.CameraCheckInUtc,attendance.CameraCheckOutUtc,
                            attendance.TotalBreakMinutes,
                            CONVERT(char(5),effective.ShiftStart,108) ShiftStartTime,
                            CONVERT(char(5),effective.ShiftEnd,108) ShiftEndTime,
                            person.TimeZoneId,person.ReportsToPersonId
                        FROM VisiblePeople visible
                        JOIN dbo.Persons person
                          ON person.PersonId=visible.PersonId AND person.IsActive=1 AND person.TenantId=@TenantId
                        JOIN dbo.StaffVacancy staff ON staff.PersonId=person.PersonId
                        JOIN dbo.Vacancies vacancy ON vacancy.VacancyId=staff.VacancyId
                        LEFT JOIN dbo.JobTitles jobTitle ON jobTitle.Id=vacancy.JobTitleId
                        LEFT JOIN dbo.OrganizationTree organization ON organization.Id=vacancy.OrganizationId
                        LEFT JOIN dbo.AttendanceMapRules mapRule
                          ON mapRule.StaffId=staff.StaffId AND mapRule.TenantId=@TenantId
                        CROSS JOIN Dates dates
                        LEFT JOIN dbo.EmployeeTimingSchedules timing
                          ON timing.StaffId=staff.StaffId
                         AND timing.ScheduleDate=dates.AttendanceDate
                         AND timing.TenantId=@TenantId
                        LEFT JOIN dbo.AppLookupValues timingHoliday
                          ON timingHoliday.LookupValueId=timing.HolidayTypeId
                        LEFT JOIN dbo.AttendanceRecords attendance
                          ON attendance.PersonId=person.PersonId
                         AND attendance.AttendanceDate=dates.AttendanceDate
                         AND attendance.TenantId=@TenantId
                        CROSS APPLY(SELECT
                            COALESCE(timing.IsOn,CASE WHEN DATENAME(weekday,dates.AttendanceDate) IN(N'Saturday',N'Sunday') THEN 0 ELSE 1 END) IsOn,
                            COALESCE(TRY_CONVERT(time(0),timing.TimeFrom),TRY_CONVERT(time(0),mapRule.TimeFrom),TRY_CONVERT(time(0),person.ShiftStartTime)) ShiftStart,
                            COALESCE(TRY_CONVERT(time(0),timing.TimeTo),TRY_CONVERT(time(0),mapRule.TimeTo),TRY_CONVERT(time(0),person.ShiftEndTime)) ShiftEnd
                        ) effective
                    )
                        SELECT rowData.Id,rowData.PersonId,rowData.EmployeeNumber,rowData.EmployeeName,
                            rowData.Department,rowData.Designation,rowData.AttendanceDate,
                            rowData.AttendanceStatusId,statusDefinition.StatusName,statusStyle.Code StatusCode,
                            color.ColorCode StatusColorCode,color.FontColor StatusFontColor,color.FontSize StatusFontSize,
                            rowData.CameraPlatformActionStatusId AS CameraAttendanceStatusId,
                            cameraStatus.Name AS CameraStatusName,
                            cameraStatusStyle.Code AS CameraStatusCode,
                            cameraColor.ColorCode AS CameraStatusColorCode,
                            cameraColor.FontColor AS CameraStatusFontColor,
                            rowData.AttendanceEntryTypeId,
                            COALESCE(entryType.Name,CASE WHEN rowData.Id IS NULL THEN noEntry.Name END) AttendanceEntryType,
                            rowData.AttendanceWorkModeId,workMode.Name AttendanceWorkMode,
                        rowData.CheckInUtc,rowData.CheckOutUtc,
                        rowData.CameraCheckInUtc,rowData.CameraCheckOutUtc,
                        rowData.TotalBreakMinutes,
                        rowData.ShiftStartTime,rowData.ShiftEndTime,rowData.TimeZoneId,rowData.ReportsToPersonId
                    FROM ReportRows rowData
                    LEFT JOIN dbo.ProcessStatusStyles statusStyle ON statusStyle.Id=rowData.AttendanceStatusId
                    LEFT JOIN dbo.Statuses statusDefinition ON statusDefinition.Id=statusStyle.StatusId
                    LEFT JOIN dbo.ColorStyles color ON color.Id=statusStyle.ColorStyleId
                    LEFT JOIN dbo.ProcessStatusStyles cameraStatusStyle ON cameraStatusStyle.Id=rowData.CameraPlatformActionStatusId
                    LEFT JOIN dbo.Statuses cameraStatus ON cameraStatus.Id=cameraStatusStyle.StatusId
                    LEFT JOIN dbo.ColorStyles cameraColor ON cameraColor.Id=cameraStatusStyle.ColorStyleId
                    LEFT JOIN dbo.AttendanceEntryTypes entryType ON entryType.Id=rowData.AttendanceEntryTypeId
                    LEFT JOIN dbo.AttendanceEntryTypes noEntry ON noEntry.Code=N'NONE' AND noEntry.IsActive=1
                    LEFT JOIN dbo.AttendanceWorkModes workMode ON workMode.Id=rowData.AttendanceWorkModeId
                    ORDER BY rowData.AttendanceDate DESC,rowData.EmployeeName OPTION(MAXRECURSION 367);
                END
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.usp_Attendance_DailyReport;");
            migrationBuilder.Sql(
                """
                IF COL_LENGTH(N'dbo.AttendanceRecords', N'CameraCheckOutUtc') IS NOT NULL
                    ALTER TABLE dbo.AttendanceRecords DROP COLUMN CameraCheckOutUtc;

                IF COL_LENGTH(N'dbo.AttendanceRecords', N'CameraCheckInUtc') IS NOT NULL
                    ALTER TABLE dbo.AttendanceRecords DROP COLUMN CameraCheckInUtc;
                """);
        }
    }
}
