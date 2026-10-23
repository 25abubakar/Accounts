namespace Accounts.Migrations;

public partial class LinkAttendanceCanonicalStatuses
{
    private const string CanonicalReportSql = """
        CREATE OR ALTER PROCEDURE dbo.usp_Attendance_DailyReport
                @TenantId int, @DateFrom date, @DateTo date, @VisiblePersonIds nvarchar(max)
            AS
            BEGIN
                SET NOCOUNT ON;
                DECLARE @ProcessId int,@DayOff int,@Holiday int,@PlatformDayOff int,@PlatformHoliday int;
                SELECT @ProcessId=Id FROM dbo.Processes WHERE ProcessName=N'Attendance';
                
                DECLARE @PlatformActionId int;
                SELECT TOP(1) @PlatformActionId=Id FROM PlatformSettings.Actions WHERE Name=N'Attendance';
        
                SELECT TOP(1) @PlatformDayOff = pas.Id
                FROM PlatformSettings.Statuses s
                JOIN PlatformSettings.ActionStatuses pas ON pas.StatusId = s.Id
                WHERE pas.ActionId = @PlatformActionId AND s.Name = N'Day Off' 
                ORDER BY pas.Id;
                
                SELECT TOP(1) @PlatformHoliday = pas.Id
                FROM PlatformSettings.Statuses s
                JOIN PlatformSettings.ActionStatuses pas ON pas.StatusId = s.Id
                WHERE pas.ActionId = @PlatformActionId AND s.Name = N'Holiday' 
                ORDER BY pas.Id;
        
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
                        COALESCE(attendance.CanonicalStatusId,CASE WHEN effective.IsOn=0 THEN
                            CASE WHEN timingHoliday.ValueCode IN(N'HOLIDAY',N'ANNUAL_HOLIDAY')
                                      OR DATENAME(weekday,dates.AttendanceDate)=N'Sunday'
                                 THEN (SELECT Id FROM PlatformSettings.Statuses WHERE Name=N'Holiday')
                                 ELSE (SELECT Id FROM PlatformSettings.Statuses WHERE Name=N'Day Off') END END) CanonicalStatusId,
                        COALESCE(attendance.PlatformActionStatusId,
                            CASE WHEN effective.IsOn=0 THEN
                                CASE WHEN timingHoliday.ValueCode IN(N'HOLIDAY',N'ANNUAL_HOLIDAY')
                                     THEN COALESCE(@PlatformHoliday,@PlatformDayOff)
                                     ELSE COALESCE(@PlatformDayOff,@PlatformHoliday) END END) PlatformActionStatusId,
                        COALESCE(attendance.AttendanceEntryTypeId,mapRule.AttendanceEntryTypeId) AttendanceEntryTypeId,
                        attendance.AttendanceWorkModeId,
                        attendance.CheckInUtc,attendance.CheckOutUtc,
                    attendance.CameraCheckInUtc,attendance.CameraCheckOutUtc,
                    attendance.CameraPlatformActionStatusId,
                    attendance.TotalBreakMinutes,
                        CONVERT(char(5),effective.ShiftStart,108) ShiftStartTime,
                        CONVERT(char(5),effective.ShiftEnd,108) ShiftEndTime,
                        person.TimeZoneId,person.ReportsToPersonId,
                        ruleSetting.AbsentAfterShiftStartMinutes,
                        ruleSetting.EarlyCheckoutAbsentAfterMinutes,
                        ruleSetting.MissingCheckoutAfterShiftEndMinutes
                    FROM VisiblePeople visible
                    JOIN dbo.Persons person
                      ON person.PersonId=visible.PersonId AND person.IsActive=1 AND person.TenantId=@TenantId
                    JOIN dbo.StaffVacancy staff ON staff.PersonId=person.PersonId AND staff.TenantId=@TenantId
                    JOIN dbo.Vacancies vacancy ON vacancy.VacancyId=staff.VacancyId
                    LEFT JOIN dbo.JobTitles jobTitle ON jobTitle.Id=vacancy.JobTitleId
                    LEFT JOIN dbo.OrganizationTree organization ON organization.Id=vacancy.OrganizationId
                    LEFT JOIN dbo.AttendanceMapRules mapRule
                      ON mapRule.StaffId=staff.StaffId AND mapRule.TenantId=@TenantId
                    LEFT JOIN dbo.AttendanceRuleSettings ruleSetting
                      ON ruleSetting.TenantId=@TenantId
                     AND ruleSetting.AttendanceEntryTypeId=mapRule.AttendanceEntryTypeId
                     AND ruleSetting.IsActive=1
                     AND ruleSetting.IsApproved=1
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
                    COALESCE(rowData.CanonicalStatusId,rowData.PlatformActionStatusId) AS AttendanceStatusId,
                    CAST(CASE WHEN rowData.CanonicalStatusId IS NOT NULL THEN 1 ELSE 0 END AS bit) IsCanonicalStatus,
                    COALESCE(canonicalStatus.Name,platformStatus.Name) StatusName,
                    CASE WHEN rowData.CanonicalStatusId IS NOT NULL THEN
                        COALESCE(canonicalCrDb.DbValue,CASE canonicalStatus.Name
                            WHEN N'Present' THEN N'P' WHEN N'T-Present' THEN N'TP'
                            WHEN N'Day Off' THEN N'DO' WHEN N'Holiday' THEN N'HO'
                            WHEN N'Absent' THEN N'A' END)
                        ELSE platformCrDb.DbValue END StatusCode,
                    CASE WHEN rowData.CanonicalStatusId IS NOT NULL THEN canonicalColor.ColorCode ELSE platformColor.ColorCode END StatusColorCode,
                    CASE WHEN rowData.CanonicalStatusId IS NOT NULL THEN canonicalColor.FontColor ELSE platformColor.FontColor END StatusFontColor,
                    '12px' StatusFontSize,
                    rowData.CameraPlatformActionStatusId AS CameraAttendanceStatusId,
                    cameraStatus.Name AS CameraStatusName,
                    cameraCrDb.DbValue AS CameraStatusCode,
                    cameraColor.ColorCode AS CameraStatusColorCode,
                    cameraColor.FontColor AS CameraStatusFontColor,
                    rowData.AttendanceEntryTypeId,
                    COALESCE(entryType.Name,CASE WHEN rowData.Id IS NULL THEN noEntry.Name END) AttendanceEntryType,
                    rowData.AttendanceWorkModeId,workMode.Name AttendanceWorkMode,
                    rowData.CheckInUtc,rowData.CheckOutUtc,
                    rowData.CameraCheckInUtc,rowData.CameraCheckOutUtc,
                    rowData.TotalBreakMinutes,
                    rowData.ShiftStartTime,rowData.ShiftEndTime,rowData.TimeZoneId,rowData.ReportsToPersonId,
                    rowData.AbsentAfterShiftStartMinutes,
                    rowData.EarlyCheckoutAbsentAfterMinutes,
                    rowData.MissingCheckoutAfterShiftEndMinutes
                FROM ReportRows rowData
                LEFT JOIN PlatformSettings.Statuses canonicalStatus ON canonicalStatus.Id=rowData.CanonicalStatusId
                LEFT JOIN PlatformSettings.Processes canonicalProcess ON canonicalProcess.Name=N'Daily Activities'
                LEFT JOIN PlatformSettings.ProcessStatuses canonicalMap
                    ON canonicalMap.ProcessId=canonicalProcess.Id AND canonicalMap.StatusId=canonicalStatus.Id
                LEFT JOIN PlatformSettings.Colors canonicalColor ON canonicalColor.Id=canonicalMap.ColorId
                LEFT JOIN PlatformSettings.StatusCrDbValues canonicalCrDb ON canonicalCrDb.StatusId=canonicalStatus.Id
                LEFT JOIN PlatformSettings.ActionStatuses platformActionStatus ON platformActionStatus.Id=rowData.PlatformActionStatusId
                LEFT JOIN PlatformSettings.Statuses platformStatus ON platformStatus.Id=platformActionStatus.StatusId
                LEFT JOIN PlatformSettings.StatusCrDbValues platformCrDb ON platformCrDb.StatusId=platformStatus.Id 
                LEFT JOIN PlatformSettings.Colors platformColor ON platformColor.Id=platformActionStatus.ColorId
                LEFT JOIN PlatformSettings.ActionStatuses cameraActionStatus ON cameraActionStatus.Id=rowData.CameraPlatformActionStatusId
                LEFT JOIN PlatformSettings.Statuses cameraStatus ON cameraStatus.Id=cameraActionStatus.StatusId
                LEFT JOIN PlatformSettings.StatusCrDbValues cameraCrDb ON cameraCrDb.StatusId=cameraStatus.Id 
                LEFT JOIN PlatformSettings.Colors cameraColor ON cameraColor.Id=cameraActionStatus.ColorId
                LEFT JOIN PlatformTypes.AttendanceTypes entryType ON entryType.Id=rowData.AttendanceEntryTypeId AND entryType.TenantId=@TenantId
                LEFT JOIN PlatformTypes.AttendanceTypes noEntry ON noEntry.Code=N'NONE' AND noEntry.IsActive=1 AND noEntry.TenantId=@TenantId
                LEFT JOIN dbo.AttendanceWorkModes workMode ON workMode.Id=rowData.AttendanceWorkModeId
                ORDER BY rowData.AttendanceDate DESC,rowData.EmployeeName OPTION(MAXRECURSION 367);
            END
        """;
    private const string LegacyReportSql = """
        CREATE OR ALTER PROCEDURE dbo.usp_Attendance_DailyReport
                @TenantId int, @DateFrom date, @DateTo date, @VisiblePersonIds nvarchar(max)
            AS
            BEGIN
                SET NOCOUNT ON;
                DECLARE @ProcessId int,@DayOff int,@Holiday int,@PlatformDayOff int,@PlatformHoliday int;
                SELECT @ProcessId=Id FROM dbo.Processes WHERE ProcessName=N'Attendance';
                
                DECLARE @PlatformActionId int;
                SELECT TOP(1) @PlatformActionId=Id FROM PlatformSettings.Actions WHERE Name=N'Attendance';
        
                SELECT TOP(1) @PlatformDayOff = pas.Id
                FROM PlatformSettings.Statuses s
                JOIN PlatformSettings.ActionStatuses pas ON pas.StatusId = s.Id
                WHERE pas.ActionId = @PlatformActionId AND s.Name = N'Day Off' 
                ORDER BY pas.Id;
                
                SELECT TOP(1) @PlatformHoliday = pas.Id
                FROM PlatformSettings.Statuses s
                JOIN PlatformSettings.ActionStatuses pas ON pas.StatusId = s.Id
                WHERE pas.ActionId = @PlatformActionId AND s.Name = N'Holiday' 
                ORDER BY pas.Id;
        
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
                        COALESCE(attendance.PlatformActionStatusId,
                            CASE WHEN effective.IsOn=0 THEN
                                CASE WHEN timingHoliday.ValueCode IN(N'HOLIDAY',N'ANNUAL_HOLIDAY')
                                     THEN COALESCE(@PlatformHoliday,@PlatformDayOff)
                                     ELSE COALESCE(@PlatformDayOff,@PlatformHoliday) END END) PlatformActionStatusId,
                        COALESCE(attendance.AttendanceEntryTypeId,mapRule.AttendanceEntryTypeId) AttendanceEntryTypeId,
                        attendance.AttendanceWorkModeId,
                        attendance.CheckInUtc,attendance.CheckOutUtc,
                    attendance.CameraCheckInUtc,attendance.CameraCheckOutUtc,
                    attendance.CameraPlatformActionStatusId,
                    attendance.TotalBreakMinutes,
                        CONVERT(char(5),effective.ShiftStart,108) ShiftStartTime,
                        CONVERT(char(5),effective.ShiftEnd,108) ShiftEndTime,
                        person.TimeZoneId,person.ReportsToPersonId,
                        ruleSetting.AbsentAfterShiftStartMinutes,
                        ruleSetting.EarlyCheckoutAbsentAfterMinutes,
                        ruleSetting.MissingCheckoutAfterShiftEndMinutes
                    FROM VisiblePeople visible
                    JOIN dbo.Persons person
                      ON person.PersonId=visible.PersonId AND person.IsActive=1 AND person.TenantId=@TenantId
                    JOIN dbo.StaffVacancy staff ON staff.PersonId=person.PersonId AND staff.TenantId=@TenantId
                    JOIN dbo.Vacancies vacancy ON vacancy.VacancyId=staff.VacancyId
                    LEFT JOIN dbo.JobTitles jobTitle ON jobTitle.Id=vacancy.JobTitleId
                    LEFT JOIN dbo.OrganizationTree organization ON organization.Id=vacancy.OrganizationId
                    LEFT JOIN dbo.AttendanceMapRules mapRule
                      ON mapRule.StaffId=staff.StaffId AND mapRule.TenantId=@TenantId
                    LEFT JOIN dbo.AttendanceRuleSettings ruleSetting
                      ON ruleSetting.TenantId=@TenantId
                     AND ruleSetting.AttendanceEntryTypeId=mapRule.AttendanceEntryTypeId
                     AND ruleSetting.IsActive=1
                     AND ruleSetting.IsApproved=1
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
                    rowData.PlatformActionStatusId AS AttendanceStatusId,
                    platformStatus.Name StatusName,
                    platformCrDb.DbValue StatusCode,
                    platformColor.ColorCode StatusColorCode,
                    platformColor.FontColor StatusFontColor,
                    '12px' StatusFontSize,
                    rowData.CameraPlatformActionStatusId AS CameraAttendanceStatusId,
                    cameraStatus.Name AS CameraStatusName,
                    cameraCrDb.DbValue AS CameraStatusCode,
                    cameraColor.ColorCode AS CameraStatusColorCode,
                    cameraColor.FontColor AS CameraStatusFontColor,
                    rowData.AttendanceEntryTypeId,
                    COALESCE(entryType.Name,CASE WHEN rowData.Id IS NULL THEN noEntry.Name END) AttendanceEntryType,
                    rowData.AttendanceWorkModeId,workMode.Name AttendanceWorkMode,
                    rowData.CheckInUtc,rowData.CheckOutUtc,
                    rowData.CameraCheckInUtc,rowData.CameraCheckOutUtc,
                    rowData.TotalBreakMinutes,
                    rowData.ShiftStartTime,rowData.ShiftEndTime,rowData.TimeZoneId,rowData.ReportsToPersonId,
                    rowData.AbsentAfterShiftStartMinutes,
                    rowData.EarlyCheckoutAbsentAfterMinutes,
                    rowData.MissingCheckoutAfterShiftEndMinutes
                FROM ReportRows rowData
                LEFT JOIN PlatformSettings.ActionStatuses platformActionStatus ON platformActionStatus.Id=rowData.PlatformActionStatusId
                LEFT JOIN PlatformSettings.Statuses platformStatus ON platformStatus.Id=platformActionStatus.StatusId
                LEFT JOIN PlatformSettings.StatusCrDbValues platformCrDb ON platformCrDb.StatusId=platformStatus.Id 
                LEFT JOIN PlatformSettings.Colors platformColor ON platformColor.Id=platformActionStatus.ColorId
                LEFT JOIN PlatformSettings.ActionStatuses cameraActionStatus ON cameraActionStatus.Id=rowData.CameraPlatformActionStatusId
                LEFT JOIN PlatformSettings.Statuses cameraStatus ON cameraStatus.Id=cameraActionStatus.StatusId
                LEFT JOIN PlatformSettings.StatusCrDbValues cameraCrDb ON cameraCrDb.StatusId=cameraStatus.Id 
                LEFT JOIN PlatformSettings.Colors cameraColor ON cameraColor.Id=cameraActionStatus.ColorId
                LEFT JOIN PlatformTypes.AttendanceTypes entryType ON entryType.Id=rowData.AttendanceEntryTypeId AND entryType.TenantId=@TenantId
                LEFT JOIN PlatformTypes.AttendanceTypes noEntry ON noEntry.Code=N'NONE' AND noEntry.IsActive=1 AND noEntry.TenantId=@TenantId
                LEFT JOIN dbo.AttendanceWorkModes workMode ON workMode.Id=rowData.AttendanceWorkModeId
                ORDER BY rowData.AttendanceDate DESC,rowData.EmployeeName OPTION(MAXRECURSION 367);
            END
        """;
}

