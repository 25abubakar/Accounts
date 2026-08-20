using Microsoft.EntityFrameworkCore;

namespace Accounts.Data;

public static class AttendanceRecordSchema
{
    private static readonly SemaphoreSlim LocalGate = new(1, 1);
    private const int CameraReportSchemaVersion = 5;
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

                    IF OBJECT_ID(N'[PlatformTypes].[AttendanceTypes]', N'U') IS NOT NULL
                       AND COL_LENGTH(N'[PlatformTypes].[AttendanceTypes]', N'Code') IS NULL
                    BEGIN
                        ALTER TABLE [PlatformTypes].[AttendanceTypes] ADD [Code] nvarchar(30) NULL;
                        UPDATE PlatformTypes.AttendanceTypes
                           SET Code = N'CHECK'
                         WHERE Name IN (N'Check In / Out', N'Check In/Out', N'Check')
                            OR Name LIKE N'%Check%In%';
                        UPDATE PlatformTypes.AttendanceTypes
                           SET Code = N'NONE'
                         WHERE Name IN (N'No attendance', N'No Attendance')
                            OR Name LIKE N'%No attendance%';
                        UPDATE PlatformTypes.AttendanceTypes
                           SET Code = N'MANUAL'
                         WHERE Name LIKE N'%Manual%';
                        UPDATE PlatformTypes.AttendanceTypes
                           SET Code = CONCAT(N'TYPE_', Id)
                         WHERE Code IS NULL OR LTRIM(RTRIM(Code)) = N'';
                        ALTER TABLE [PlatformTypes].[AttendanceTypes] ALTER COLUMN [Code] nvarchar(30) NOT NULL;
                        IF NOT EXISTS (
                            SELECT 1
                            FROM sys.indexes
                            WHERE name = N'UQ_AttendanceTypes_Code'
                              AND object_id = OBJECT_ID(N'PlatformTypes.AttendanceTypes'))
                            CREATE UNIQUE INDEX UQ_AttendanceTypes_Code ON PlatformTypes.AttendanceTypes(Code);
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

                        SELECT TOP(1) @PlatformDayOff = pas.Id
                        FROM PlatformSettings.Statuses s
                        JOIN PlatformSettings.ActionStatuses pas ON pas.StatusId = s.Id
                        WHERE pas.ActionId = @PlatformActionId AND s.Name = N''Day Off'' AND (pas.TenantId = @TenantId OR pas.TenantId IS NULL)
                        ORDER BY CASE WHEN pas.TenantId=@TenantId THEN 0 ELSE 1 END;
                        
                        SELECT TOP(1) @PlatformHoliday = pas.Id
                        FROM PlatformSettings.Statuses s
                        JOIN PlatformSettings.ActionStatuses pas ON pas.StatusId = s.Id
                        WHERE pas.ActionId = @PlatformActionId AND s.Name = N''Holiday'' AND (pas.TenantId = @TenantId OR pas.TenantId IS NULL)
                        ORDER BY CASE WHEN pas.TenantId=@TenantId THEN 0 ELSE 1 END;

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
                        LEFT JOIN PlatformSettings.StatusCrDbValues platformCrDb ON platformCrDb.StatusId=platformStatus.Id AND (platformCrDb.TenantId=@TenantId OR platformCrDb.TenantId IS NULL)
                        LEFT JOIN PlatformSettings.Colors platformColor ON platformColor.Id=platformActionStatus.ColorId
                        LEFT JOIN PlatformSettings.ActionStatuses cameraActionStatus ON cameraActionStatus.Id=rowData.CameraPlatformActionStatusId
                        LEFT JOIN PlatformSettings.Statuses cameraStatus ON cameraStatus.Id=cameraActionStatus.StatusId
                        LEFT JOIN PlatformSettings.StatusCrDbValues cameraCrDb ON cameraCrDb.StatusId=cameraStatus.Id AND (cameraCrDb.TenantId=@TenantId OR cameraCrDb.TenantId IS NULL)
                        LEFT JOIN PlatformSettings.Colors cameraColor ON cameraColor.Id=cameraActionStatus.ColorId
                        LEFT JOIN PlatformTypes.AttendanceTypes entryType ON entryType.Id=rowData.AttendanceEntryTypeId AND entryType.TenantId=@TenantId
                        LEFT JOIN PlatformTypes.AttendanceTypes noEntry ON noEntry.Code=N''NONE'' AND noEntry.IsActive=1 AND noEntry.TenantId=@TenantId
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
                            CAST(NULL AS int) AS [Month],
                            CAST(NULL AS int) AS [Year],
                            CAST(NULL AS decimal(18,2)) AS PerDay,
                            CAST(NULL AS decimal(18,2)) AS PerHour,
                            CAST(NULL AS int) AS MonthWorkingDays,
                            CAST(NULL AS int) AS MonthWorkingMinutes,
                            CAST(NULL AS int) AS MonthAttendanceMinutes,
                            CAST(NULL AS int) AS NetShortMinutes,
                            CAST(NULL AS int) AS NetOvertimeMinutes,
                            CAST(NULL AS decimal(18,2)) AS NetDeduction,
                            CAST(NULL AS decimal(18,2)) AS OvertimeBonusAmount,
                            CAST(NULL AS bit) AS IsOvertimeApproved,
                            CAST(NULL AS decimal(18,2)) AS FinalSalary
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
                            COALESCE(hr.CurrentPay, hr.BasicSalary, 0) AS CurrentPay,
                            ruleSetting.WorkingMinutes AS RuleWorkingMinutes
                        FROM VisiblePeople visible
                        JOIN dbo.Persons person ON person.PersonId = visible.PersonId AND person.TenantId = @TenantId AND person.IsActive = 1
                        JOIN dbo.StaffVacancy staff ON staff.PersonId = person.PersonId AND staff.TenantId = @TenantId
                        LEFT JOIN dbo.Vacancies vacancy ON vacancy.VacancyId = staff.VacancyId
                        LEFT JOIN dbo.JobTitles jobTitle ON jobTitle.Id = vacancy.JobTitleId
                        LEFT JOIN dbo.OrganizationTree organization ON organization.Id = vacancy.OrganizationId
                        LEFT JOIN dbo.PersonHrProfiles hr ON hr.PersonId = person.PersonId AND hr.TenantId = @TenantId
                        LEFT JOIN dbo.AttendanceMapRules mapRule ON mapRule.StaffId = staff.StaffId AND mapRule.TenantId = @TenantId
                        LEFT JOIN dbo.AttendanceRuleSettings ruleSetting ON ruleSetting.TenantId = @TenantId AND ruleSetting.AttendanceEntryTypeId = mapRule.AttendanceEntryTypeId AND ruleSetting.IsActive = 1 AND ruleSetting.IsApproved = 1
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
                            staff.CurrentPay,
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
                            END AS TotalAttendanceMinutes
                        FROM StaffRows staff
                        CROSS JOIN Dates dates
                        LEFT JOIN dbo.EmployeeTimingSchedules schedule ON schedule.StaffId = staff.StaffId AND schedule.ScheduleDate = dates.AttendanceDate AND schedule.TenantId = @TenantId
                        LEFT JOIN dbo.AttendanceRecords attendance ON attendance.PersonId = staff.PersonId AND attendance.AttendanceDate = dates.AttendanceDate AND attendance.TenantId = @TenantId
                    ),
                    AggregatedRows AS
                    (
                        SELECT
                            PersonId,
                            StaffId,
                            MAX(StaffNumber) AS StaffNumber,
                            MAX(EmployeeName) AS EmployeeName,
                            MAX(JobTitle) AS JobTitle,
                            MAX(Department) AS Department,
                            MAX(CurrentPay) AS CurrentPay,
                            SUM(CASE WHEN TotalWorkingMinutes > 0 THEN 1 ELSE 0 END) AS MonthWorkingDays,
                            SUM(TotalWorkingMinutes) AS MonthWorkingMinutes,
                            SUM(TotalAttendanceMinutes) AS MonthAttendanceMinutes
                        FROM BaseRows
                        GROUP BY PersonId, StaffId
                    ),
                    CalculatedRows AS
                    (
                        SELECT
                            agg.*,
                            CAST(CASE WHEN agg.MonthWorkingDays > 0 AND agg.CurrentPay > 0 THEN agg.CurrentPay / agg.MonthWorkingDays ELSE 0 END AS decimal(18,2)) AS PerDay,
                            CAST(
                                CASE WHEN agg.MonthWorkingDays > 0 AND agg.CurrentPay > 0 AND agg.MonthWorkingMinutes > 0
                                     THEN (agg.CurrentPay / agg.MonthWorkingDays) / (agg.MonthWorkingMinutes / CAST(agg.MonthWorkingDays AS float) / 60.0)
                                     ELSE 0
                                END AS decimal(18,2)
                            ) AS PerHour,
                            CASE WHEN agg.MonthWorkingMinutes > agg.MonthAttendanceMinutes THEN agg.MonthWorkingMinutes - agg.MonthAttendanceMinutes ELSE 0 END AS NetShortMinutes,
                            CASE WHEN agg.MonthAttendanceMinutes > agg.MonthWorkingMinutes THEN agg.MonthAttendanceMinutes - agg.MonthWorkingMinutes ELSE 0 END AS NetOvertimeMinutes
                        FROM AggregatedRows agg
                    )
                    SELECT
                        CAST(ROW_NUMBER() OVER (ORDER BY c.EmployeeName) AS bigint) AS Id,
                        c.PersonId,
                        c.StaffId,
                        c.StaffNumber,
                        c.EmployeeName,
                        c.JobTitle,
                        c.Department,
                        @Month AS [Month],
                        @Year AS [Year],
                        c.PerDay,
                        c.PerHour,
                        c.MonthWorkingDays,
                        c.MonthWorkingMinutes,
                        c.MonthAttendanceMinutes,
                        c.NetShortMinutes,
                        c.NetOvertimeMinutes,
                        CAST((c.NetShortMinutes / 60.0) * c.PerHour AS decimal(18,2)) AS NetDeduction,
                        CAST((c.NetOvertimeMinutes / 60.0) * c.PerHour AS decimal(18,2)) AS OvertimeBonusAmount,
                        CAST(COALESCE(s.IsOvertimeApproved, 0) AS bit) AS IsOvertimeApproved,
                        CAST(c.CurrentPay - ((c.NetShortMinutes / 60.0) * c.PerHour) + (CASE WHEN s.IsOvertimeApproved = 1 THEN (c.NetOvertimeMinutes / 60.0) * c.PerHour ELSE 0 END) AS decimal(18,2)) AS FinalSalary
                    FROM CalculatedRows c
                    LEFT JOIN dbo.AttendanceMonthlySettlements s ON s.PersonId = c.PersonId AND s.SettlementYear = @Year AND s.SettlementMonth = @Month AND s.TenantId = @TenantId
                    ORDER BY c.EmployeeName;
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
