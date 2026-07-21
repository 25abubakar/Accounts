using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260721130000_StaffBasedEmployeeTimingSchedules")]
public sealed class StaffBasedEmployeeTimingSchedules : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'dbo.EmployeeTimingSchedules', N'U') IS NOT NULL
            BEGIN
                IF OBJECT_ID(N'dbo.CK_EmployeeTimingSchedules_RequiredWeekend', N'C') IS NOT NULL
                    ALTER TABLE dbo.EmployeeTimingSchedules DROP CONSTRAINT CK_EmployeeTimingSchedules_RequiredWeekend;

                IF OBJECT_ID(N'dbo.FK_EmployeeTimingSchedules_Persons_PersonId', N'F') IS NOT NULL
                    ALTER TABLE dbo.EmployeeTimingSchedules DROP CONSTRAINT FK_EmployeeTimingSchedules_Persons_PersonId;

                IF OBJECT_ID(N'dbo.FK_EmployeeTimingSchedules_StaffVacancy_StaffId', N'F') IS NOT NULL
                    ALTER TABLE dbo.EmployeeTimingSchedules DROP CONSTRAINT FK_EmployeeTimingSchedules_StaffVacancy_StaffId;

                IF OBJECT_ID(N'dbo.FK_EmployeeTimingSchedules_AppLookupValues_HolidayTypeId', N'F') IS NOT NULL
                    ALTER TABLE dbo.EmployeeTimingSchedules DROP CONSTRAINT FK_EmployeeTimingSchedules_AppLookupValues_HolidayTypeId;

                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_EmployeeTimingSchedules_PersonId_ScheduleDate' AND object_id = OBJECT_ID(N'dbo.EmployeeTimingSchedules'))
                    DROP INDEX IX_EmployeeTimingSchedules_PersonId_ScheduleDate ON dbo.EmployeeTimingSchedules;

                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.EmployeeTimingSchedules') AND name = N'StaffId')
                    ALTER TABLE dbo.EmployeeTimingSchedules ADD StaffId uniqueidentifier NULL;

                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.EmployeeTimingSchedules') AND name = N'HolidayTypeId')
                    ALTER TABLE dbo.EmployeeTimingSchedules ADD HolidayTypeId int NULL;

                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.EmployeeTimingSchedules') AND name = N'ScheduleMonth')
                    ALTER TABLE dbo.EmployeeTimingSchedules ADD ScheduleMonth int NULL;

                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.EmployeeTimingSchedules') AND name = N'ScheduleYear')
                    ALTER TABLE dbo.EmployeeTimingSchedules ADD ScheduleYear int NULL;

                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.EmployeeTimingSchedules') AND name = N'WorkingMinutes')
                    ALTER TABLE dbo.EmployeeTimingSchedules ADD WorkingMinutes int NOT NULL CONSTRAINT DF_EmployeeTimingSchedules_WorkingMinutes DEFAULT(0);
            END
            """);

        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'dbo.EmployeeTimingSchedules', N'U') IS NOT NULL
            BEGIN
                UPDATE schedule
                   SET StaffId = staff.StaffId
                FROM dbo.EmployeeTimingSchedules schedule
                JOIN dbo.StaffVacancy staff
                  ON staff.PersonId = schedule.PersonId
                 AND staff.TenantId = schedule.TenantId
                WHERE schedule.StaffId IS NULL
                  AND COL_LENGTH(N'dbo.EmployeeTimingSchedules', N'PersonId') IS NOT NULL;

                UPDATE dbo.EmployeeTimingSchedules
                   SET ScheduleMonth = MONTH(ScheduleDate),
                       ScheduleYear = YEAR(ScheduleDate)
                WHERE ScheduleMonth IS NULL OR ScheduleYear IS NULL;

                UPDATE schedule
                   SET HolidayTypeId = COALESCE(lookup.LookupValueId, working.LookupValueId)
                FROM dbo.EmployeeTimingSchedules schedule
                LEFT JOIN dbo.AppLookupTypes lookupType
                  ON lookupType.LookupTypeCode = N'TIMING_HOLIDAY_TYPE'
                OUTER APPLY (
                    SELECT CASE
                        WHEN COL_LENGTH(N'dbo.EmployeeTimingSchedules', N'HolidayType') IS NULL THEN N'WORKING_DAY'
                        WHEN schedule.HolidayType IN (N'PUBLIC_HOLIDAY', N'COMPANY_HOLIDAY') THEN N'HOLIDAY'
                        WHEN NULLIF(LTRIM(RTRIM(schedule.HolidayType)), N'') IS NULL THEN N'WORKING_DAY'
                        ELSE schedule.HolidayType
                    END AS Code
                ) normalized
                LEFT JOIN dbo.AppLookupValues lookup
                  ON lookup.LookupTypeId = lookupType.LookupTypeId
                 AND lookup.ValueCode = normalized.Code
                LEFT JOIN dbo.AppLookupValues working
                  ON working.LookupTypeId = lookupType.LookupTypeId
                 AND working.ValueCode = N'WORKING_DAY'
                WHERE schedule.HolidayTypeId IS NULL;

                UPDATE schedule
                   SET WorkingMinutes = CASE
                       WHEN schedule.IsOn = 1
                        AND TRY_CONVERT(time(0), schedule.TimeFrom) IS NOT NULL
                        AND TRY_CONVERT(time(0), schedule.TimeTo) IS NOT NULL
                       THEN CASE
                           WHEN DATEDIFF(minute, TRY_CONVERT(time(0), schedule.TimeFrom), TRY_CONVERT(time(0), schedule.TimeTo)) > 0
                           THEN DATEDIFF(minute, TRY_CONVERT(time(0), schedule.TimeFrom), TRY_CONVERT(time(0), schedule.TimeTo))
                           ELSE DATEDIFF(minute, TRY_CONVERT(time(0), schedule.TimeFrom), TRY_CONVERT(time(0), schedule.TimeTo)) + 1440
                       END
                       ELSE 0
                   END
                FROM dbo.EmployeeTimingSchedules schedule;

                DECLARE @TimingLookupTypeId int =
                    (SELECT TOP(1) LookupTypeId FROM dbo.AppLookupTypes WHERE LookupTypeCode = N'TIMING_HOLIDAY_TYPE');
                DECLARE @SaturdayDayOffId int =
                    (SELECT TOP(1) LookupValueId FROM dbo.AppLookupValues WHERE LookupTypeId = @TimingLookupTypeId AND ValueCode = N'DAY_OFF');
                DECLARE @SundayHolidayId int =
                    (SELECT TOP(1) LookupValueId FROM dbo.AppLookupValues WHERE LookupTypeId = @TimingLookupTypeId AND ValueCode = N'HOLIDAY');

                UPDATE dbo.EmployeeTimingSchedules
                   SET HolidayTypeId = COALESCE(@SaturdayDayOffId, HolidayTypeId),
                       IsOn = 0,
                       TimeFrom = NULL,
                       TimeTo = NULL,
                       WorkingMinutes = 0
                WHERE ((DATEDIFF(day, '19000101', ScheduleDate) % 7 + 7) % 7) = 5;

                UPDATE dbo.EmployeeTimingSchedules
                   SET HolidayTypeId = COALESCE(@SundayHolidayId, HolidayTypeId),
                       IsOn = 0,
                       TimeFrom = NULL,
                       TimeTo = NULL,
                       WorkingMinutes = 0
                WHERE ((DATEDIFF(day, '19000101', ScheduleDate) % 7 + 7) % 7) = 6;

                IF EXISTS (
                    SELECT 1 FROM dbo.EmployeeTimingSchedules
                    WHERE StaffId IS NULL OR HolidayTypeId IS NULL OR ScheduleMonth IS NULL OR ScheduleYear IS NULL
                )
                    THROW 51030, 'Employee timing schedule migration failed because one or more rows could not be mapped to StaffId or holiday type.', 1;

                ALTER TABLE dbo.EmployeeTimingSchedules ALTER COLUMN StaffId uniqueidentifier NOT NULL;
                ALTER TABLE dbo.EmployeeTimingSchedules ALTER COLUMN HolidayTypeId int NOT NULL;
                ALTER TABLE dbo.EmployeeTimingSchedules ALTER COLUMN ScheduleMonth int NOT NULL;
                ALTER TABLE dbo.EmployeeTimingSchedules ALTER COLUMN ScheduleYear int NOT NULL;

                IF COL_LENGTH(N'dbo.EmployeeTimingSchedules', N'HolidayType') IS NOT NULL
                    ALTER TABLE dbo.EmployeeTimingSchedules DROP COLUMN HolidayType;

                IF COL_LENGTH(N'dbo.EmployeeTimingSchedules', N'PersonId') IS NOT NULL
                    ALTER TABLE dbo.EmployeeTimingSchedules DROP COLUMN PersonId;

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_EmployeeTimingSchedules_StaffId_ScheduleDate' AND object_id = OBJECT_ID(N'dbo.EmployeeTimingSchedules'))
                    CREATE UNIQUE INDEX IX_EmployeeTimingSchedules_StaffId_ScheduleDate ON dbo.EmployeeTimingSchedules(StaffId, ScheduleDate);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_EmployeeTimingSchedules_StaffId_ScheduleYear_ScheduleMonth' AND object_id = OBJECT_ID(N'dbo.EmployeeTimingSchedules'))
                    CREATE INDEX IX_EmployeeTimingSchedules_StaffId_ScheduleYear_ScheduleMonth ON dbo.EmployeeTimingSchedules(StaffId, ScheduleYear, ScheduleMonth);

                IF OBJECT_ID(N'dbo.FK_EmployeeTimingSchedules_StaffVacancy_StaffId', N'F') IS NULL
                    ALTER TABLE dbo.EmployeeTimingSchedules
                    ADD CONSTRAINT FK_EmployeeTimingSchedules_StaffVacancy_StaffId
                    FOREIGN KEY (StaffId) REFERENCES dbo.StaffVacancy(StaffId);

                IF OBJECT_ID(N'dbo.FK_EmployeeTimingSchedules_AppLookupValues_HolidayTypeId', N'F') IS NULL
                    ALTER TABLE dbo.EmployeeTimingSchedules
                    ADD CONSTRAINT FK_EmployeeTimingSchedules_AppLookupValues_HolidayTypeId
                    FOREIGN KEY (HolidayTypeId) REFERENCES dbo.AppLookupValues(LookupValueId);

                ALTER TABLE dbo.EmployeeTimingSchedules
                ADD CONSTRAINT CK_EmployeeTimingSchedules_RequiredWeekend
                CHECK (
                    (((DATEDIFF(day,'19000101',[ScheduleDate]) % 7 + 7) % 7) NOT IN (5,6))
                    OR (((DATEDIFF(day,'19000101',[ScheduleDate]) % 7 + 7) % 7) IN (5,6)
                        AND [IsOn] = 0 AND [TimeFrom] IS NULL AND [TimeTo] IS NULL AND [WorkingMinutes] = 0)
                );
            END
            """);

        migrationBuilder.Sql(
            """
            CREATE OR ALTER PROCEDURE dbo.usp_Attendance_EvaluateStatuses
                @TenantId int, @DateFrom date, @DateTo date, @AsOfUtc datetime2
            AS
            BEGIN
                SET NOCOUNT ON; SET XACT_ABORT ON;
                DECLARE @PolicyId int,@TimeZoneId nvarchar(100),@Grace int,@AbsentAfter int,@MissingOutAfter int,@Tolerance int,
                    @Present int,@Late int,@CompletedLate int,@ShortLeave int,@EarlyDeparture int,@Absent int,
                    @DayOff int,@Holiday int,@NowLocal datetime2,@ProcessId int;
                SELECT TOP(1) @PolicyId=Id,@TimeZoneId=TimeZoneId,@Grace=OnTimeGraceMinutesAfter,
                    @AbsentAfter=AbsentAfterShiftStartMinutes,@MissingOutAfter=MissingCheckoutAfterShiftEndMinutes,
                    @Tolerance=FullDayToleranceMinutes,@Present=PresentStatusId,@Late=LateStatusId,
                    @CompletedLate=CompletedLateStatusId,@ShortLeave=ShortLeaveStatusId,
                    @EarlyDeparture=EarlyDepartureStatusId,@Absent=AbsentStatusId
                FROM dbo.AttendancePolicies WHERE IsActive=1 AND (TenantId=@TenantId OR TenantId IS NULL)
                ORDER BY CASE WHEN TenantId=@TenantId THEN 0 ELSE 1 END;
                IF @PolicyId IS NULL THROW 51000,'No active attendance policy is configured.',1;

                SELECT @ProcessId=Id FROM dbo.Processes WHERE ProcessName=N'Attendance';
                SELECT TOP(1) @DayOff=Id FROM dbo.ProcessStatusStyles
                    WHERE ProcessId=@ProcessId AND Code=N'DO' AND IsActive=1 AND (TenantId=@TenantId OR TenantId IS NULL)
                    ORDER BY CASE WHEN TenantId=@TenantId THEN 0 ELSE 1 END;
                SELECT TOP(1) @Holiday=Id FROM dbo.ProcessStatusStyles
                    WHERE ProcessId=@ProcessId AND Code=N'H' AND IsActive=1 AND (TenantId=@TenantId OR TenantId IS NULL)
                    ORDER BY CASE WHEN TenantId=@TenantId THEN 0 ELSE 1 END;
                SET @NowLocal=CONVERT(datetime2,(@AsOfUtc AT TIME ZONE 'UTC') AT TIME ZONE @TimeZoneId);

                ;WITH Dates AS(
                    SELECT @DateFrom D UNION ALL SELECT DATEADD(day,1,D) FROM Dates WHERE D<@DateTo
                ), EffectiveDays AS(
                    SELECT person.TenantId,person.PersonId,dates.D,mapRule.IsOpenAttendance,mapType.Code AttendanceTypeCode,
                        COALESCE(timing.IsOn,CASE WHEN DATENAME(weekday,dates.D) IN(N'Saturday',N'Sunday') THEN 0 ELSE 1 END) IsOn,
                        COALESCE(TRY_CONVERT(time(0),timing.TimeFrom),TRY_CONVERT(time(0),mapRule.TimeFrom),TRY_CONVERT(time(0),person.ShiftStartTime)) ShiftStart
                    FROM dbo.Persons person
                    JOIN dbo.StaffVacancy staff ON staff.PersonId=person.PersonId
                    JOIN dbo.AttendanceMapRules mapRule
                      ON mapRule.StaffId=staff.StaffId AND mapRule.TenantId=@TenantId
                    JOIN dbo.AttendanceEntryTypes mapType ON mapType.Id=mapRule.AttendanceEntryTypeId AND mapType.IsActive=1
                    CROSS JOIN Dates dates
                    LEFT JOIN dbo.EmployeeTimingSchedules timing
                      ON timing.StaffId=staff.StaffId AND timing.ScheduleDate=dates.D AND timing.TenantId=@TenantId
                    WHERE person.TenantId=@TenantId AND person.IsActive=1
                ), Missing AS(
                    SELECT TenantId,PersonId,D
                    FROM EffectiveDays dayInfo
                    WHERE dayInfo.IsOn=1
                      AND dayInfo.IsOpenAttendance=0
                      AND dayInfo.AttendanceTypeCode<>N'NONE'
                      AND DATEADD(minute,@AbsentAfter,
                          DATEADD(minute,DATEDIFF(minute,'00:00',dayInfo.ShiftStart),CONVERT(datetime2,dayInfo.D)))<=@NowLocal
                      AND NOT EXISTS(
                          SELECT 1 FROM dbo.AttendanceRecords attendance
                          WHERE attendance.PersonId=dayInfo.PersonId AND attendance.AttendanceDate=dayInfo.D)
                )
                INSERT dbo.AttendanceRecords
                    (TenantId,PersonId,AttendanceDate,AttendanceStatusId,TotalBreakMinutes,CreatedDate,ModifiedDate)
                SELECT TenantId,PersonId,D,@Absent,0,@AsOfUtc,@AsOfUtc
                FROM Missing OPTION(MAXRECURSION 367);

                UPDATE attendance SET AttendanceStatusId=CASE
                    WHEN effective.IsOn=0 AND attendance.CheckInUtc IS NULL THEN
                        CASE WHEN timingHoliday.ValueCode IN(N'HOLIDAY',N'ANNUAL_HOLIDAY')
                             THEN COALESCE(@Holiday,@DayOff,attendance.AttendanceStatusId)
                             ELSE COALESCE(@DayOff,@Holiday,attendance.AttendanceStatusId) END
                    WHEN attendance.AttendanceStatusId=@ShortLeave THEN @ShortLeave
                    WHEN attendance.CheckInUtc IS NULL THEN @Absent
                    WHEN attendance.CheckOutUtc IS NULL
                      AND DATEADD(minute,@MissingOutAfter,
                          DATEADD(day,CASE WHEN effective.ShiftEnd<=effective.ShiftStart THEN 1 ELSE 0 END,
                              DATEADD(minute,DATEDIFF(minute,'00:00',effective.ShiftEnd),CONVERT(datetime2,attendance.AttendanceDate))))<=@NowLocal
                        THEN @Absent
                    WHEN attendance.CheckOutUtc IS NULL
                      AND CONVERT(time,(@AsOfUtc AT TIME ZONE 'UTC') AT TIME ZONE @TimeZoneId)>
                          CONVERT(time,DATEADD(minute,@Grace,effective.ShiftStart)) THEN @Late
                    WHEN attendance.CheckOutUtc IS NULL THEN @Present
                    WHEN DATEDIFF(minute,attendance.CheckInUtc,attendance.CheckOutUtc)-attendance.TotalBreakMinutes
                      < COALESCE(NULLIF(timing.WorkingMinutes,0),
                          (CASE WHEN DATEDIFF(minute,effective.ShiftStart,effective.ShiftEnd)>0
                                THEN DATEDIFF(minute,effective.ShiftStart,effective.ShiftEnd)
                                ELSE DATEDIFF(minute,effective.ShiftStart,effective.ShiftEnd)+1440 END)) - @Tolerance
                        THEN @EarlyDeparture
                    WHEN CONVERT(time,(attendance.CheckInUtc AT TIME ZONE 'UTC') AT TIME ZONE @TimeZoneId)>
                          CONVERT(time,DATEADD(minute,@Grace,effective.ShiftStart)) THEN @CompletedLate
                    ELSE @Present END,
                    ModifiedDate=@AsOfUtc
                FROM dbo.AttendanceRecords attendance
                JOIN dbo.Persons person ON person.PersonId=attendance.PersonId
                LEFT JOIN dbo.StaffVacancy staff ON staff.PersonId=person.PersonId
                LEFT JOIN dbo.AttendanceMapRules mapRule
                  ON mapRule.StaffId=staff.StaffId AND mapRule.TenantId=@TenantId
                LEFT JOIN dbo.EmployeeTimingSchedules timing
                  ON timing.StaffId=staff.StaffId
                 AND timing.ScheduleDate=attendance.AttendanceDate
                 AND timing.TenantId=@TenantId
                LEFT JOIN dbo.AppLookupValues timingHoliday
                  ON timingHoliday.LookupValueId=timing.HolidayTypeId
                CROSS APPLY(SELECT
                    COALESCE(timing.IsOn,CASE WHEN DATENAME(weekday,attendance.AttendanceDate) IN(N'Saturday',N'Sunday') THEN 0 ELSE 1 END) IsOn,
                    COALESCE(TRY_CONVERT(time(0),timing.TimeFrom),TRY_CONVERT(time(0),mapRule.TimeFrom),TRY_CONVERT(time(0),person.ShiftStartTime)) ShiftStart,
                    COALESCE(TRY_CONVERT(time(0),timing.TimeTo),TRY_CONVERT(time(0),mapRule.TimeTo),TRY_CONVERT(time(0),person.ShiftEndTime)) ShiftEnd
                ) effective
                WHERE attendance.TenantId=@TenantId
                  AND attendance.AttendanceDate BETWEEN @DateFrom AND @DateTo;
            END
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
                        attendance.CheckInUtc,attendance.CheckOutUtc,attendance.TotalBreakMinutes,
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
                    rowData.AttendanceEntryTypeId,
                    COALESCE(entryType.Name,CASE WHEN rowData.Id IS NULL THEN noEntry.Name END) AttendanceEntryType,
                    rowData.AttendanceWorkModeId,workMode.Name AttendanceWorkMode,
                    rowData.CheckInUtc,rowData.CheckOutUtc,rowData.TotalBreakMinutes,
                    rowData.ShiftStartTime,rowData.ShiftEndTime,rowData.TimeZoneId,rowData.ReportsToPersonId
                FROM ReportRows rowData
                LEFT JOIN dbo.ProcessStatusStyles statusStyle ON statusStyle.Id=rowData.AttendanceStatusId
                LEFT JOIN dbo.Statuses statusDefinition ON statusDefinition.Id=statusStyle.StatusId
                LEFT JOIN dbo.ColorStyles color ON color.Id=statusStyle.ColorStyleId
                LEFT JOIN dbo.AttendanceEntryTypes entryType ON entryType.Id=rowData.AttendanceEntryTypeId
                LEFT JOIN dbo.AttendanceEntryTypes noEntry ON noEntry.Code=N'NONE' AND noEntry.IsActive=1
                LEFT JOIN dbo.AttendanceWorkModes workMode ON workMode.Id=rowData.AttendanceWorkModeId
                ORDER BY rowData.AttendanceDate DESC,rowData.EmployeeName OPTION(MAXRECURSION 367);
            END
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Employee timing schedules are now staff-based and this migration is intentionally not reversible.");
}
