using Microsoft.EntityFrameworkCore;

namespace Accounts.Data;

public static class AttendanceRecordSchema
{
    private static readonly SemaphoreSlim LocalGate = new(1, 1);
    private const int CameraReportSchemaVersion = 2;
    private static int AppliedCameraReportSchemaVersion;
    private static bool DeductionReportProcedureEnsured;

    public static void ResetCameraReportSchema() => AppliedCameraReportSchemaVersion = 0;

    public static async Task EnsureCameraColumnsAsync(ApplicationDbContext db, CancellationToken ct = default)
    {
        if (AppliedCameraReportSchemaVersion >= CameraReportSchemaVersion) return;

        await LocalGate.WaitAsync(ct);
        try
        {
            if (AppliedCameraReportSchemaVersion >= CameraReportSchemaVersion) return;

            await db.Database.ExecuteSqlRawAsync(
                """
                DECLARE @lockResult int;
                EXEC @lockResult = sp_getapplock
                    @Resource = N'Accounts.AttendanceRecords.CameraColumns',
                    @LockMode = N'Exclusive',
                    @LockOwner = N'Session',
                    @LockTimeout = 15000;

                IF @lockResult < 0
                    THROW 51040, N'Could not acquire AttendanceRecords camera schema lock.', 1;

                BEGIN TRY
                    IF OBJECT_ID(N'[dbo].[AttendanceRecords]', N'U') IS NOT NULL
                    BEGIN
                        IF COL_LENGTH(N'[dbo].[AttendanceRecords]', N'CameraCheckInUtc') IS NULL
                            ALTER TABLE [dbo].[AttendanceRecords] ADD [CameraCheckInUtc] datetime2 NULL;

                        IF COL_LENGTH(N'[dbo].[AttendanceRecords]', N'CameraCheckOutUtc') IS NULL
                            ALTER TABLE [dbo].[AttendanceRecords] ADD [CameraCheckOutUtc] datetime2 NULL;

                        IF COL_LENGTH(N'[dbo].[AttendanceRecords]', N'PlatformActionStatusId') IS NULL
                            ALTER TABLE [dbo].[AttendanceRecords] ADD [PlatformActionStatusId] int NULL;
                    END;

                    IF OBJECT_ID(N'[dbo].[AttendanceRuleSettings]', N'U') IS NOT NULL
                       AND COL_LENGTH(N'[dbo].[AttendanceRuleSettings]', N'EarlyCheckoutAbsentAfterMinutes') IS NULL
                    BEGIN
                        ALTER TABLE [dbo].[AttendanceRuleSettings]
                            ADD [EarlyCheckoutAbsentAfterMinutes] int NOT NULL
                            CONSTRAINT DF_AttendanceRuleSettings_EarlyCheckoutAbsentAfterMinutes DEFAULT(120);
                    END;

                    IF OBJECT_ID(N'[dbo].[AttendanceEntryTypes]', N'U') IS NOT NULL
                       AND COL_LENGTH(N'[dbo].[AttendanceEntryTypes]', N'Code') IS NULL
                    BEGIN
                        ALTER TABLE [dbo].[AttendanceEntryTypes] ADD [Code] nvarchar(30) NULL;
                        UPDATE dbo.AttendanceEntryTypes
                           SET Code = N'CHECK'
                         WHERE Name IN (N'Check In / Out', N'Check In/Out', N'Check')
                            OR Name LIKE N'%Check%In%';
                        UPDATE dbo.AttendanceEntryTypes
                           SET Code = N'NONE'
                         WHERE Name IN (N'No attendance', N'No Attendance')
                            OR Name LIKE N'%No attendance%';
                        UPDATE dbo.AttendanceEntryTypes
                           SET Code = N'MANUAL'
                         WHERE Name LIKE N'%Manual%';
                        UPDATE dbo.AttendanceEntryTypes
                           SET Code = CONCAT(N'TYPE_', Id)
                         WHERE Code IS NULL OR LTRIM(RTRIM(Code)) = N'';
                        ALTER TABLE [dbo].[AttendanceEntryTypes] ALTER COLUMN [Code] nvarchar(30) NOT NULL;
                        IF NOT EXISTS (
                            SELECT 1
                            FROM sys.indexes
                            WHERE name = N'UQ_AttendanceEntryTypes_Code'
                              AND object_id = OBJECT_ID(N'dbo.AttendanceEntryTypes'))
                            CREATE UNIQUE INDEX UQ_AttendanceEntryTypes_Code ON dbo.AttendanceEntryTypes(Code);
                    END;

                    IF OBJECT_ID(N'[dbo].[AttendanceWorkModes]', N'U') IS NOT NULL
                       AND COL_LENGTH(N'[dbo].[AttendanceWorkModes]', N'Code') IS NULL
                    BEGIN
                        ALTER TABLE [dbo].[AttendanceWorkModes] ADD [Code] nvarchar(30) NULL;
                        UPDATE dbo.AttendanceWorkModes
                           SET Code = CONCAT(N'MODE_', Id)
                         WHERE Code IS NULL OR LTRIM(RTRIM(Code)) = N'';
                        ALTER TABLE [dbo].[AttendanceWorkModes] ALTER COLUMN [Code] nvarchar(30) NOT NULL;
                        IF NOT EXISTS (
                            SELECT 1
                            FROM sys.indexes
                            WHERE name = N'UQ_AttendanceWorkModes_Code'
                              AND object_id = OBJECT_ID(N'dbo.AttendanceWorkModes'))
                            CREATE UNIQUE INDEX UQ_AttendanceWorkModes_Code ON dbo.AttendanceWorkModes(Code);
                    END;

                    IF OBJECT_ID(N'[dbo].[AttendanceEntryTypes]', N'U') IS NOT NULL
                    BEGIN
                        IF NOT EXISTS (SELECT 1 FROM dbo.AttendanceEntryTypes WHERE Code = N'CHECK')
                            INSERT dbo.AttendanceEntryTypes(Code, Name, IsActive) VALUES (N'CHECK', N'Check In / Out', 1);
                        IF NOT EXISTS (SELECT 1 FROM dbo.AttendanceEntryTypes WHERE Code = N'NONE')
                            INSERT dbo.AttendanceEntryTypes(Code, Name, IsActive) VALUES (N'NONE', N'No attendance', 1);
                        IF NOT EXISTS (SELECT 1 FROM dbo.AttendanceEntryTypes WHERE Code = N'MANUAL')
                            INSERT dbo.AttendanceEntryTypes(Code, Name, IsActive) VALUES (N'MANUAL', N'Manual', 1);
                    END;

                    EXEC(N'
                    CREATE OR ALTER PROCEDURE dbo.usp_Attendance_DailyReport
                        @TenantId int, @DateFrom date, @DateTo date, @VisiblePersonIds nvarchar(max)
                    AS
                    BEGIN
                        SET NOCOUNT ON;
                        DECLARE @ProcessId int,@DayOff int,@Holiday int,@PlatformDayOff int,@PlatformHoliday int;
                        SELECT @ProcessId=Id FROM dbo.Processes WHERE ProcessName=N''Attendance'';
                        
                        DECLARE @PlatformActionId int;
                        SELECT TOP(1) @PlatformActionId=Id FROM PlatformSettings.Actions WHERE Name=N''Attendance'' AND TenantId=@TenantId;

                        SELECT TOP(1) @DayOff=pss.Id, @PlatformDayOff=pas.Id
                        FROM dbo.ProcessStatusStyles pss
                        INNER JOIN PlatformSettings.StatusCrDbValues crdb ON crdb.DbValue=pss.Code AND (crdb.TenantId=pss.TenantId OR crdb.TenantId IS NULL)
                        INNER JOIN PlatformSettings.ActionStatuses pas ON pas.StatusId=crdb.StatusId AND pas.ActionId=@PlatformActionId
                        WHERE pss.ProcessId=@ProcessId AND pss.Code=N''DO'' AND pss.IsActive=1 AND (pss.TenantId=@TenantId OR pss.TenantId IS NULL)
                        ORDER BY CASE WHEN pss.TenantId=@TenantId THEN 0 ELSE 1 END;
                        
                        SELECT TOP(1) @Holiday=pss.Id, @PlatformHoliday=pas.Id
                        FROM dbo.ProcessStatusStyles pss
                        INNER JOIN PlatformSettings.StatusCrDbValues crdb ON crdb.DbValue=pss.Code AND (crdb.TenantId=pss.TenantId OR crdb.TenantId IS NULL)
                        INNER JOIN PlatformSettings.ActionStatuses pas ON pas.StatusId=crdb.StatusId AND pas.ActionId=@PlatformActionId
                        WHERE pss.ProcessId=@ProcessId AND pss.Code=N''H'' AND pss.IsActive=1 AND (pss.TenantId=@TenantId OR pss.TenantId IS NULL)
                        ORDER BY CASE WHEN pss.TenantId=@TenantId THEN 0 ELSE 1 END;

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
                                COALESCE(jobTitle.TitleName,vacancy.JobTitle,N'''') Designation,
                                dates.AttendanceDate,
                                COALESCE(attendance.PlatformActionStatusId,
                                    CASE WHEN effective.IsOn=0 THEN
                                        CASE WHEN timingHoliday.ValueCode IN(N''HOLIDAY'',N''ANNUAL_HOLIDAY'')
                                             THEN COALESCE(@PlatformHoliday,@PlatformDayOff)
                                             ELSE COALESCE(@PlatformDayOff,@PlatformHoliday) END END) PlatformActionStatusId,
                                COALESCE(attendance.AttendanceEntryTypeId,mapRule.AttendanceEntryTypeId) AttendanceEntryTypeId,
                                attendance.AttendanceWorkModeId,
                                attendance.CheckInUtc,attendance.CheckOutUtc,
                                attendance.CameraCheckInUtc,attendance.CameraCheckOutUtc,
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
                                COALESCE(timing.IsOn,CASE WHEN DATENAME(weekday,dates.AttendanceDate) IN(N''Saturday'',N''Sunday'') THEN 0 ELSE 1 END) IsOn,
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
                            ''12px'' StatusFontSize,
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
                        LEFT JOIN PlatformSettings.StatusCrDbValues platformCrDb ON platformCrDb.StatusId=platformStatus.Id AND (platformCrDb.TenantId=@TenantId OR platformCrDb.TenantId IS NULL)
                        LEFT JOIN PlatformSettings.Colors platformColor ON platformColor.Id=platformActionStatus.ColorId
                        LEFT JOIN dbo.AttendanceEntryTypes entryType ON entryType.Id=rowData.AttendanceEntryTypeId
                        LEFT JOIN dbo.AttendanceEntryTypes noEntry ON noEntry.Code=N''NONE'' AND noEntry.IsActive=1
                        LEFT JOIN dbo.AttendanceWorkModes workMode ON workMode.Id=rowData.AttendanceWorkModeId
                        ORDER BY rowData.AttendanceDate DESC,rowData.EmployeeName OPTION(MAXRECURSION 367);
                    END');

                    EXEC sp_releaseapplock
                        @Resource = N'Accounts.AttendanceRecords.CameraColumns',
                        @LockOwner = N'Session';
                END TRY
                BEGIN CATCH
                    EXEC sp_releaseapplock
                        @Resource = N'Accounts.AttendanceRecords.CameraColumns',
                        @LockOwner = N'Session';
                    THROW;
                END CATCH;
                """,
                ct);

            AppliedCameraReportSchemaVersion = CameraReportSchemaVersion;
        }
        finally
        {
            LocalGate.Release();
        }
    }

    public static async Task EnsureDeductionReportProcedureAsync(ApplicationDbContext db, CancellationToken ct = default)
    {
        if (DeductionReportProcedureEnsured) return;

        await LocalGate.WaitAsync(ct);
        try
        {
            if (DeductionReportProcedureEnsured) return;

            await db.Database.ExecuteSqlRawAsync(
                """
                CREATE OR ALTER PROCEDURE dbo.usp_Attendance_DeductionReport
                    @TenantId int,
                    @Year int,
                    @Month int,
                    @VisiblePersonIds nvarchar(max)
                AS
                BEGIN
                    SET NOCOUNT ON;

                    DECLARE @DateFrom date = DATEFROMPARTS(@Year, @Month, 1);
                    DECLARE @DateTo date = EOMONTH(@DateFrom);
                    DECLARE @Today date = CAST(SYSDATETIME() AS date);
                    DECLARE @LastReportDate date = CASE WHEN @DateTo < @Today THEN @DateTo ELSE @Today END;

                    IF @DateFrom > @Today
                    BEGIN
                        SELECT
                            CAST(NULL AS bigint) AS Id,
                            CAST(NULL AS uniqueidentifier) AS PersonId,
                            CAST(NULL AS uniqueidentifier) AS StaffId,
                            CAST(N'' AS nvarchar(50)) AS StaffNumber,
                            CAST(N'' AS nvarchar(200)) AS EmployeeName,
                            CAST(N'' AS nvarchar(150)) AS JobTitle,
                            CAST(N'' AS nvarchar(200)) AS Department,
                            CAST(NULL AS int) AS [Day],
                            CAST(NULL AS int) AS [Month],
                            CAST(NULL AS int) AS [Year],
                            CAST(NULL AS int) AS TotalWorkingMinutes,
                            CAST(NULL AS int) AS TotalAttendanceMinutes,
                            CAST(NULL AS int) AS HoursDiffMinutes,
                            CAST(NULL AS int) AS DeductionMinutes,
                            CAST(NULL AS decimal(18,2)) AS DeductionDays,
                            CAST(NULL AS int) AS HoursAdjustMinutes,
                            CAST(NULL AS int) AS NetStandardMinutes,
                            CAST(NULL AS decimal(18,2)) AS GrossDeduction,
                            CAST(NULL AS decimal(18,2)) AS AdjustAmount,
                            CAST(NULL AS decimal(18,2)) AS NetDeduction,
                            CAST(NULL AS decimal(18,2)) AS PerHour,
                            CAST(NULL AS decimal(18,2)) AS PerDay,
                            CAST(NULL AS bit) AS Approved,
                            CAST(NULL AS bit) AS Pending
                        WHERE 1 = 0;
                        RETURN;
                    END

                    ;WITH Dates AS
                    (
                        SELECT @DateFrom AS AttendanceDate
                        UNION ALL
                        SELECT DATEADD(day, 1, AttendanceDate)
                        FROM Dates
                        WHERE AttendanceDate < @LastReportDate
                    ),
                    VisiblePeople AS
                    (
                        SELECT TRY_CONVERT(uniqueidentifier, [value]) AS PersonId
                        FROM OPENJSON(@VisiblePersonIds)
                        WHERE TRY_CONVERT(uniqueidentifier, [value]) IS NOT NULL
                    ),
                    StaffRows AS
                    (
                        SELECT
                            person.PersonId,
                            staff.StaffId,
                            COALESCE(staff.LoginId, vacancy.VacancyCode, N'') AS StaffNumber,
                            person.FullName AS EmployeeName,
                            COALESCE(jobTitle.TitleName, vacancy.JobTitle, N'') AS JobTitle,
                            COALESCE(vacancy.Department, organization.Name, N'') AS Department,
                            COALESCE(NULLIF(person.ShiftStartTime, N''), N'09:00') AS ShiftStartTime,
                            COALESCE(NULLIF(person.ShiftEndTime, N''), N'18:00') AS ShiftEndTime,
                            COALESCE(hr.AccountsPerDay, hr.PostingPerDay, 0) AS ProfilePerDay,
                            COALESCE(hr.AccountsPerHour, hr.PostingPerHour, 0) AS ProfilePerHour,
                            COALESCE(hr.CurrentPay, hr.BasicSalary, 0) AS CurrentPay,
                            ruleSetting.WorkingMinutes AS RuleWorkingMinutes,
                            COALESCE(ruleSetting.AdjustAbsentDays, 0) AS AdjustAbsentDays,
                            COALESCE(ruleSetting.WeekendChargeValue, 0) AS WeekendChargeValue
                        FROM VisiblePeople visible
                        JOIN dbo.Persons person
                          ON person.PersonId = visible.PersonId
                         AND person.TenantId = @TenantId
                         AND person.IsActive = 1
                        JOIN dbo.StaffVacancy staff
                          ON staff.PersonId = person.PersonId
                         AND staff.TenantId = @TenantId
                        LEFT JOIN dbo.Vacancies vacancy
                          ON vacancy.VacancyId = staff.VacancyId
                        LEFT JOIN dbo.JobTitles jobTitle
                          ON jobTitle.Id = vacancy.JobTitleId
                        LEFT JOIN dbo.OrganizationTree organization
                          ON organization.Id = vacancy.OrganizationId
                        LEFT JOIN dbo.PersonHrProfiles hr
                          ON hr.PersonId = person.PersonId
                         AND hr.TenantId = @TenantId
                        LEFT JOIN dbo.AttendanceMapRules mapRule
                          ON mapRule.StaffId = staff.StaffId
                         AND mapRule.TenantId = @TenantId
                        LEFT JOIN dbo.AttendanceRuleSettings ruleSetting
                          ON ruleSetting.TenantId = @TenantId
                         AND ruleSetting.AttendanceEntryTypeId = mapRule.AttendanceEntryTypeId
                         AND ruleSetting.IsActive = 1
                         AND ruleSetting.IsApproved = 1
                    ),
                    BaseRows AS
                    (
                        SELECT
                            staff.PersonId,
                            staff.StaffId,
                            staff.StaffNumber,
                            staff.EmployeeName,
                            staff.JobTitle,
                            staff.Department,
                            DAY(dates.AttendanceDate) AS [Day],
                            MONTH(dates.AttendanceDate) AS [Month],
                            YEAR(dates.AttendanceDate) AS [Year],
                            dates.AttendanceDate,
                            CASE
                                WHEN COALESCE(schedule.IsOn, CASE WHEN ((DATEDIFF(day, '19000101', dates.AttendanceDate) % 7 + 7) % 7) IN (5,6) THEN 0 ELSE 1 END) = 0 THEN 0
                                WHEN COALESCE(schedule.WorkingMinutes, 0) > 0 THEN schedule.WorkingMinutes
                                WHEN COALESCE(staff.RuleWorkingMinutes, 0) > 0 THEN staff.RuleWorkingMinutes
                                ELSE
                                    CASE
                                        WHEN TRY_CONVERT(time(0), COALESCE(schedule.TimeTo, staff.ShiftEndTime)) >= TRY_CONVERT(time(0), COALESCE(schedule.TimeFrom, staff.ShiftStartTime))
                                            THEN DATEDIFF(minute, TRY_CONVERT(time(0), COALESCE(schedule.TimeFrom, staff.ShiftStartTime)), TRY_CONVERT(time(0), COALESCE(schedule.TimeTo, staff.ShiftEndTime)))
                                        ELSE 1440 - DATEDIFF(minute, TRY_CONVERT(time(0), COALESCE(schedule.TimeTo, staff.ShiftEndTime)), TRY_CONVERT(time(0), COALESCE(schedule.TimeFrom, staff.ShiftStartTime)))
                                    END
                            END AS TotalWorkingMinutes,
                            CASE
                                WHEN attendance.CheckInUtc IS NOT NULL AND attendance.CheckOutUtc IS NOT NULL
                                    THEN
                                        CASE WHEN DATEDIFF(minute, attendance.CheckInUtc, attendance.CheckOutUtc) - COALESCE(attendance.TotalBreakMinutes, 0) > 0
                                             THEN DATEDIFF(minute, attendance.CheckInUtc, attendance.CheckOutUtc) - COALESCE(attendance.TotalBreakMinutes, 0)
                                             ELSE 0 END
                                ELSE 0
                            END AS TotalAttendanceMinutes,
                            staff.AdjustAbsentDays,
                            staff.WeekendChargeValue,
                            staff.ProfilePerDay,
                            staff.ProfilePerHour,
                            staff.CurrentPay
                        FROM StaffRows staff
                        CROSS JOIN Dates dates
                        LEFT JOIN dbo.EmployeeTimingSchedules schedule
                          ON schedule.StaffId = staff.StaffId
                         AND schedule.ScheduleDate = dates.AttendanceDate
                         AND schedule.TenantId = @TenantId
                        LEFT JOIN dbo.AttendanceRecords attendance
                          ON attendance.PersonId = staff.PersonId
                         AND attendance.AttendanceDate = dates.AttendanceDate
                         AND attendance.TenantId = @TenantId
                    ),
                    RankedRows AS
                    (
                        SELECT
                            base.*,
                            CASE WHEN base.TotalWorkingMinutes > 0 AND base.TotalAttendanceMinutes = 0 THEN
                                ROW_NUMBER() OVER (PARTITION BY base.PersonId, base.[Year], base.[Month], CASE WHEN base.TotalWorkingMinutes > 0 AND base.TotalAttendanceMinutes = 0 THEN 1 ELSE 0 END ORDER BY base.AttendanceDate)
                            ELSE 2147483647 END AS AbsentRank
                        FROM BaseRows base
                    ),
                    CalculatedRows AS
                    (
                        SELECT
                            ranked.*,
                            CASE WHEN ranked.TotalWorkingMinutes - ranked.TotalAttendanceMinutes > 0
                                 THEN ranked.TotalWorkingMinutes - ranked.TotalAttendanceMinutes
                                 ELSE 0 END AS HoursDiffMinutes,
                            CASE WHEN ranked.TotalWorkingMinutes > 0 AND ranked.TotalAttendanceMinutes = 0 AND ranked.AbsentRank <= ranked.AdjustAbsentDays
                                 THEN ranked.TotalWorkingMinutes
                                 ELSE 0 END AS HoursAdjustMinutes,
                            COALESCE(NULLIF(ranked.ProfilePerDay, 0), CASE WHEN ranked.CurrentPay > 0 THEN ranked.CurrentPay / 26 ELSE 0 END) AS PerDay,
                            COALESCE(NULLIF(ranked.ProfilePerHour, 0),
                                CASE
                                    WHEN COALESCE(NULLIF(ranked.ProfilePerDay, 0), CASE WHEN ranked.CurrentPay > 0 THEN ranked.CurrentPay / 26 ELSE 0 END) > 0
                                         AND ranked.TotalWorkingMinutes > 0
                                    THEN COALESCE(NULLIF(ranked.ProfilePerDay, 0), ranked.CurrentPay / 26) / (ranked.TotalWorkingMinutes / 60.0)
                                    ELSE 0
                                END) AS PerHour
                        FROM RankedRows ranked
                    ),
                    DailyAmounts AS
                    (
                        SELECT
                            calculated.PersonId,
                            calculated.StaffId,
                            calculated.StaffNumber,
                            calculated.EmployeeName,
                            calculated.JobTitle,
                            calculated.Department,
                            calculated.TotalWorkingMinutes,
                            calculated.TotalAttendanceMinutes,
                            calculated.HoursDiffMinutes,
                            CASE WHEN calculated.HoursDiffMinutes - calculated.HoursAdjustMinutes > 0
                                 THEN calculated.HoursDiffMinutes - calculated.HoursAdjustMinutes
                                 ELSE 0 END AS DeductionMinutes,
                            CAST(
                                CASE
                                    WHEN calculated.TotalWorkingMinutes <= 0 THEN 0
                                    ELSE
                                        ((CASE WHEN calculated.HoursDiffMinutes - calculated.HoursAdjustMinutes > 0
                                               THEN calculated.HoursDiffMinutes - calculated.HoursAdjustMinutes
                                               ELSE 0 END) / CAST(calculated.TotalWorkingMinutes AS decimal(18,4)))
                                        + CASE WHEN calculated.TotalAttendanceMinutes = 0
                                                    AND ((DATEDIFF(day, '19000101', calculated.AttendanceDate) % 7 + 7) % 7) IN (0,4)
                                               THEN calculated.WeekendChargeValue
                                               ELSE 0 END
                                END AS decimal(18,2)) AS DeductionDays,
                            calculated.HoursAdjustMinutes,
                            CASE WHEN calculated.TotalWorkingMinutes - calculated.HoursAdjustMinutes > 0
                                 THEN calculated.TotalWorkingMinutes - calculated.HoursAdjustMinutes
                                 ELSE 0 END AS NetStandardMinutes,
                            CAST(
                                (CASE WHEN calculated.HoursDiffMinutes - calculated.HoursAdjustMinutes > 0
                                      THEN (calculated.HoursDiffMinutes - calculated.HoursAdjustMinutes) / 60.0
                                      ELSE 0 END) * calculated.PerHour
                                + CASE WHEN calculated.TotalAttendanceMinutes = 0
                                            AND ((DATEDIFF(day, '19000101', calculated.AttendanceDate) % 7 + 7) % 7) IN (0,4)
                                       THEN calculated.WeekendChargeValue * calculated.PerDay
                                       ELSE 0 END
                                AS decimal(18,2)) AS GrossDeduction,
                            CAST(calculated.PerHour AS decimal(18,2)) AS PerHour,
                            CAST(calculated.PerDay AS decimal(18,2)) AS PerDay
                        FROM CalculatedRows calculated
                        WHERE calculated.TotalWorkingMinutes > 0 OR calculated.TotalAttendanceMinutes > 0
                    ),
                    AggregatedRows AS
                    (
                        SELECT
                            daily.PersonId,
                            daily.StaffId,
                            daily.StaffNumber,
                            daily.EmployeeName,
                            daily.JobTitle,
                            daily.Department,
                            DAY(@LastReportDate) AS [Day],
                            @Month AS [Month],
                            @Year AS [Year],
                            SUM(daily.TotalWorkingMinutes) AS TotalWorkingMinutes,
                            SUM(daily.TotalAttendanceMinutes) AS TotalAttendanceMinutes,
                            SUM(daily.HoursDiffMinutes) AS HoursDiffMinutes,
                            SUM(daily.DeductionMinutes) AS DeductionMinutes,
                            CAST(SUM(daily.DeductionDays) AS decimal(18,2)) AS DeductionDays,
                            SUM(daily.HoursAdjustMinutes) AS HoursAdjustMinutes,
                            SUM(daily.NetStandardMinutes) AS NetStandardMinutes,
                            CAST(SUM(daily.GrossDeduction) AS decimal(18,2)) AS GrossDeduction,
                            CAST(0 AS decimal(18,2)) AS AdjustAmount,
                            CAST(SUM(daily.GrossDeduction) AS decimal(18,2)) AS NetDeduction,
                            CAST(MAX(daily.PerHour) AS decimal(18,2)) AS PerHour,
                            CAST(MAX(daily.PerDay) AS decimal(18,2)) AS PerDay,
                            CAST(0 AS bit) AS Approved,
                            CAST(CASE WHEN SUM(daily.DeductionMinutes) > 0 OR SUM(daily.GrossDeduction) > 0 THEN 1 ELSE 0 END AS bit) AS Pending
                        FROM DailyAmounts daily
                        GROUP BY
                            daily.PersonId,
                            daily.StaffId,
                            daily.StaffNumber,
                            daily.EmployeeName,
                            daily.JobTitle,
                            daily.Department
                    )
                    SELECT
                        ROW_NUMBER() OVER (ORDER BY monthly.EmployeeName, monthly.StaffNumber) AS Id,
                        monthly.PersonId,
                        monthly.StaffId,
                        monthly.StaffNumber,
                        monthly.EmployeeName,
                        monthly.JobTitle,
                        monthly.Department,
                        monthly.[Day],
                        monthly.[Month],
                        monthly.[Year],
                        monthly.TotalWorkingMinutes,
                        monthly.TotalAttendanceMinutes,
                        monthly.HoursDiffMinutes,
                        monthly.DeductionMinutes,
                        monthly.DeductionDays,
                        monthly.HoursAdjustMinutes,
                        monthly.NetStandardMinutes,
                        monthly.GrossDeduction,
                        monthly.AdjustAmount,
                        monthly.NetDeduction,
                        monthly.PerHour,
                        monthly.PerDay,
                        monthly.Approved,
                        monthly.Pending
                    FROM AggregatedRows monthly
                    ORDER BY monthly.EmployeeName, monthly.StaffNumber
                    OPTION (MAXRECURSION 31);
                END
                """,
                ct);

            DeductionReportProcedureEnsured = true;
        }
        finally
        {
            LocalGate.Release();
        }
    }

    public static async Task EnsureDeductionRequestTableAsync(ApplicationDbContext db, CancellationToken ct = default)
    {
        await LocalGate.WaitAsync(ct);
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                IF OBJECT_ID(N'dbo.AttendanceDeductionRequests', N'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.AttendanceDeductionRequests
                    (
                        Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_AttendanceDeductionRequests PRIMARY KEY,
                        TenantId int NOT NULL,
                        RegNo nvarchar(50) NULL,
                        Name nvarchar(200) NOT NULL,
                        UserId nvarchar(100) NOT NULL,
                        DateOfBirth date NULL,
                        Phone nvarchar(50) NULL,
                        Email nvarchar(256) NULL,
                        Office nvarchar(150) NULL,
                        Department nvarchar(150) NULL,
                        Designation nvarchar(150) NULL,
                        Classification nvarchar(100) NULL,
                        Routing nvarchar(150) NULL,
                        Authority nvarchar(150) NULL,
                        Subject nvarchar(250) NULL,
                        DocumentName nvarchar(260) NULL,
                        DeductionMonth int NOT NULL,
                        DeductionYear int NOT NULL,
                        ActionRouting nvarchar(150) NULL,
                        ActionName nvarchar(100) NULL,
                        Comments nvarchar(1000) NULL,
                        CreatedByUserId nvarchar(450) NULL,
                        CreatedDate datetime2 NOT NULL CONSTRAINT DF_AttendanceDeductionRequests_CreatedDate DEFAULT(SYSUTCDATETIME()),
                        CONSTRAINT FK_AttendanceDeductionRequests_Tenants FOREIGN KEY(TenantId) REFERENCES dbo.Tenants(Id),
                        CONSTRAINT CK_AttendanceDeductionRequests_Month CHECK(DeductionMonth BETWEEN 1 AND 12),
                        CONSTRAINT CK_AttendanceDeductionRequests_Year CHECK(DeductionYear BETWEEN 2000 AND 2100)
                    );
                END;

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AttendanceDeductionRequests_Tenant_Period' AND object_id = OBJECT_ID(N'dbo.AttendanceDeductionRequests'))
                    CREATE INDEX IX_AttendanceDeductionRequests_Tenant_Period ON dbo.AttendanceDeductionRequests(TenantId, DeductionYear, DeductionMonth);
                """,
                ct);
        }
        finally
        {
            LocalGate.Release();
        }
    }
}
