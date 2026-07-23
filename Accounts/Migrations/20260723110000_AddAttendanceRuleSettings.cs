using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260723110000_AddAttendanceRuleSettings")]
public sealed class AddAttendanceRuleSettings : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'dbo.AttendanceRuleSettings', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.AttendanceRuleSettings
                (
                    Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_AttendanceRuleSettings PRIMARY KEY,
                    TenantId int NOT NULL,
                    AttendanceEntryTypeId int NOT NULL,
                    Reference nvarchar(50) NOT NULL,
                    RuleName nvarchar(150) NOT NULL,
                    WorkingMinutes int NOT NULL CONSTRAINT DF_AttendanceRuleSettings_WorkingMinutes DEFAULT(540),
                    BeforeCheckInMinutes int NOT NULL CONSTRAINT DF_AttendanceRuleSettings_BeforeCheckInMinutes DEFAULT(5),
                    AfterCheckOutMinutes int NOT NULL CONSTRAINT DF_AttendanceRuleSettings_AfterCheckOutMinutes DEFAULT(0),
                    CheckInAdjustMinutes int NOT NULL CONSTRAINT DF_AttendanceRuleSettings_CheckInAdjustMinutes DEFAULT(5),
                    CheckOutAdjustMinutes int NOT NULL CONSTRAINT DF_AttendanceRuleSettings_CheckOutAdjustMinutes DEFAULT(5),
                    AbsentAfterShiftStartMinutes int NOT NULL CONSTRAINT DF_AttendanceRuleSettings_AbsentAfterShiftStartMinutes DEFAULT(120),
                    MissingCheckoutAfterShiftEndMinutes int NOT NULL CONSTRAINT DF_AttendanceRuleSettings_MissingCheckoutAfterShiftEndMinutes DEFAULT(120),
                    AccountLockAbsentDays int NOT NULL CONSTRAINT DF_AttendanceRuleSettings_AccountLockAbsentDays DEFAULT(0),
                    WeekendChargeValue decimal(6,2) NOT NULL CONSTRAINT DF_AttendanceRuleSettings_WeekendChargeValue DEFAULT(0),
                    AdjustAbsentDays int NOT NULL CONSTRAINT DF_AttendanceRuleSettings_AdjustAbsentDays DEFAULT(0),
                    IsApproved bit NOT NULL CONSTRAINT DF_AttendanceRuleSettings_IsApproved DEFAULT(0),
                    IsActive bit NOT NULL CONSTRAINT DF_AttendanceRuleSettings_IsActive DEFAULT(1),
                    Remarks nvarchar(500) NULL,
                    CreatedByUserId nvarchar(450) NULL,
                    ModifiedByUserId nvarchar(450) NULL,
                    CreatedDate datetime2 NOT NULL CONSTRAINT DF_AttendanceRuleSettings_CreatedDate DEFAULT(SYSUTCDATETIME()),
                    ModifiedDate datetime2 NULL,
                    CONSTRAINT FK_AttendanceRuleSettings_Tenants FOREIGN KEY(TenantId) REFERENCES dbo.Tenants(Id),
                    CONSTRAINT FK_AttendanceRuleSettings_AttendanceEntryTypes FOREIGN KEY(AttendanceEntryTypeId) REFERENCES dbo.AttendanceEntryTypes(Id),
                    CONSTRAINT CK_AttendanceRuleSettings_Minutes CHECK
                    (
                        WorkingMinutes BETWEEN 0 AND 1440
                        AND BeforeCheckInMinutes BETWEEN 0 AND 720
                        AND AfterCheckOutMinutes BETWEEN 0 AND 720
                        AND CheckInAdjustMinutes BETWEEN 0 AND 720
                        AND CheckOutAdjustMinutes BETWEEN 0 AND 720
                        AND AbsentAfterShiftStartMinutes BETWEEN 1 AND 1440
                        AND MissingCheckoutAfterShiftEndMinutes BETWEEN 1 AND 1440
                        AND AccountLockAbsentDays BETWEEN 0 AND 31
                        AND WeekendChargeValue BETWEEN 0 AND 31
                        AND AdjustAbsentDays BETWEEN 0 AND 31
                    )
                );
            END

            IF OBJECT_ID(N'dbo.AttendanceRuleSettings', N'U') IS NOT NULL
               AND EXISTS (
                    SELECT 1
                    FROM sys.check_constraints
                    WHERE name = N'CK_AttendanceRuleSettings_Minutes'
                      AND parent_object_id = OBJECT_ID(N'dbo.AttendanceRuleSettings'))
                ALTER TABLE dbo.AttendanceRuleSettings DROP CONSTRAINT CK_AttendanceRuleSettings_Minutes;

            IF OBJECT_ID(N'dbo.AttendanceRuleSettings', N'U') IS NOT NULL
            BEGIN
                IF COL_LENGTH(N'dbo.AttendanceRuleSettings', N'AccountLocked') IS NOT NULL
                   AND COL_LENGTH(N'dbo.AttendanceRuleSettings', N'AccountLockAbsentDays') IS NULL
                    EXEC sp_rename N'dbo.AttendanceRuleSettings.AccountLocked', N'AccountLockAbsentDays', N'COLUMN';

                IF COL_LENGTH(N'dbo.AttendanceRuleSettings', N'WeekendCharged') IS NOT NULL
                   AND COL_LENGTH(N'dbo.AttendanceRuleSettings', N'WeekendChargeValue') IS NULL
                    EXEC sp_rename N'dbo.AttendanceRuleSettings.WeekendCharged', N'WeekendChargeValue', N'COLUMN';

                IF COL_LENGTH(N'dbo.AttendanceRuleSettings', N'AdjustAbsent') IS NOT NULL
                   AND COL_LENGTH(N'dbo.AttendanceRuleSettings', N'AdjustAbsentDays') IS NULL
                    EXEC sp_rename N'dbo.AttendanceRuleSettings.AdjustAbsent', N'AdjustAbsentDays', N'COLUMN';

                IF COL_LENGTH(N'dbo.AttendanceRuleSettings', N'AccountLockAbsentDays') IS NULL
                    ALTER TABLE dbo.AttendanceRuleSettings ADD AccountLockAbsentDays int NOT NULL CONSTRAINT DF_AttendanceRuleSettings_AccountLockAbsentDays DEFAULT(0);
                IF COL_LENGTH(N'dbo.AttendanceRuleSettings', N'WeekendChargeValue') IS NULL
                    ALTER TABLE dbo.AttendanceRuleSettings ADD WeekendChargeValue decimal(6,2) NOT NULL CONSTRAINT DF_AttendanceRuleSettings_WeekendChargeValue DEFAULT(0);
                IF COL_LENGTH(N'dbo.AttendanceRuleSettings', N'AdjustAbsentDays') IS NULL
                    ALTER TABLE dbo.AttendanceRuleSettings ADD AdjustAbsentDays int NOT NULL CONSTRAINT DF_AttendanceRuleSettings_AdjustAbsentDays DEFAULT(0);

                EXEC(N'ALTER TABLE dbo.AttendanceRuleSettings ALTER COLUMN AccountLockAbsentDays int NOT NULL');
                EXEC(N'ALTER TABLE dbo.AttendanceRuleSettings ALTER COLUMN WeekendChargeValue decimal(6,2) NOT NULL');
                EXEC(N'ALTER TABLE dbo.AttendanceRuleSettings ALTER COLUMN AdjustAbsentDays int NOT NULL');
            END

            IF OBJECT_ID(N'dbo.AttendanceRuleSettings', N'U') IS NOT NULL
               AND NOT EXISTS (
                    SELECT 1
                    FROM sys.check_constraints
                    WHERE name = N'CK_AttendanceRuleSettings_Minutes'
                      AND parent_object_id = OBJECT_ID(N'dbo.AttendanceRuleSettings'))
                EXEC(N'ALTER TABLE dbo.AttendanceRuleSettings ADD CONSTRAINT CK_AttendanceRuleSettings_Minutes CHECK
                (
                    WorkingMinutes BETWEEN 0 AND 1440
                    AND BeforeCheckInMinutes BETWEEN 0 AND 720
                    AND AfterCheckOutMinutes BETWEEN 0 AND 720
                    AND CheckInAdjustMinutes BETWEEN 0 AND 720
                    AND CheckOutAdjustMinutes BETWEEN 0 AND 720
                    AND AbsentAfterShiftStartMinutes BETWEEN 1 AND 1440
                    AND MissingCheckoutAfterShiftEndMinutes BETWEEN 1 AND 1440
                    AND AccountLockAbsentDays BETWEEN 0 AND 31
                    AND WeekendChargeValue BETWEEN 0 AND 31
                    AND AdjustAbsentDays BETWEEN 0 AND 31
                )');

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_AttendanceRuleSettings_Tenant_AttendanceType' AND object_id = OBJECT_ID(N'dbo.AttendanceRuleSettings'))
                CREATE UNIQUE INDEX UX_AttendanceRuleSettings_Tenant_AttendanceType
                ON dbo.AttendanceRuleSettings(TenantId, AttendanceEntryTypeId);

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AttendanceRuleSettings_Tenant_ActiveApproved' AND object_id = OBJECT_ID(N'dbo.AttendanceRuleSettings'))
                EXEC(N'CREATE INDEX IX_AttendanceRuleSettings_Tenant_ActiveApproved
                ON dbo.AttendanceRuleSettings(TenantId, IsActive, IsApproved)
                INCLUDE (AttendanceEntryTypeId, WorkingMinutes, BeforeCheckInMinutes, CheckInAdjustMinutes, CheckOutAdjustMinutes, AbsentAfterShiftStartMinutes, MissingCheckoutAfterShiftEndMinutes, AccountLockAbsentDays, WeekendChargeValue, AdjustAbsentDays)');
            """);

        migrationBuilder.Sql(
            """
            CREATE OR ALTER VIEW dbo.vw_AttendanceRuleSettings
            AS
            SELECT ruleSetting.Id,
                   ruleSetting.TenantId,
                   ruleSetting.AttendanceEntryTypeId,
                   entryType.Code AS AttendanceTypeCode,
                   entryType.Name AS AttendanceTypeName,
                   ruleSetting.Reference,
                   ruleSetting.RuleName,
                   ruleSetting.WorkingMinutes,
                   ruleSetting.BeforeCheckInMinutes,
                   ruleSetting.AfterCheckOutMinutes,
                   ruleSetting.CheckInAdjustMinutes,
                   ruleSetting.CheckOutAdjustMinutes,
                   ruleSetting.AbsentAfterShiftStartMinutes,
                   ruleSetting.MissingCheckoutAfterShiftEndMinutes,
                   ruleSetting.AccountLockAbsentDays,
                   ruleSetting.WeekendChargeValue,
                   ruleSetting.AdjustAbsentDays,
                   ruleSetting.IsApproved,
                   ruleSetting.IsActive,
                   ruleSetting.Remarks
            FROM dbo.AttendanceRuleSettings AS ruleSetting
            JOIN dbo.AttendanceEntryTypes AS entryType
              ON entryType.Id = ruleSetting.AttendanceEntryTypeId;
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
                        COALESCE(TRY_CONVERT(time(0),timing.TimeFrom),TRY_CONVERT(time(0),mapRule.TimeFrom),TRY_CONVERT(time(0),person.ShiftStartTime)) ShiftStart,
                        COALESCE(setting.AbsentAfterShiftStartMinutes,@AbsentAfter) AbsentAfterMinutes,
                        COALESCE(setting.AdjustAbsentDays,0) AdjustAbsentDays,
                        (
                            SELECT COUNT_BIG(1)
                            FROM dbo.AttendanceRecords monthlyAbsent
                            WHERE monthlyAbsent.TenantId=person.TenantId
                              AND monthlyAbsent.PersonId=person.PersonId
                              AND monthlyAbsent.AttendanceStatusId=@Absent
                              AND monthlyAbsent.AttendanceDate>=DATEFROMPARTS(YEAR(dates.D),MONTH(dates.D),1)
                              AND monthlyAbsent.AttendanceDate<dates.D
                        ) PriorMonthlyAbsentCount
                    FROM dbo.Persons person
                    JOIN dbo.StaffVacancy staff ON staff.PersonId=person.PersonId
                    JOIN dbo.AttendanceMapRules mapRule
                      ON mapRule.StaffId=staff.StaffId AND mapRule.TenantId=@TenantId
                    JOIN dbo.AttendanceEntryTypes mapType ON mapType.Id=mapRule.AttendanceEntryTypeId AND mapType.IsActive=1
                    CROSS JOIN Dates dates
                    LEFT JOIN dbo.AttendanceRuleSettings setting
                      ON setting.TenantId=@TenantId
                     AND setting.AttendanceEntryTypeId=mapRule.AttendanceEntryTypeId
                     AND setting.IsActive=1
                     AND setting.IsApproved=1
                    LEFT JOIN dbo.EmployeeTimingSchedules timing
                      ON timing.StaffId=staff.StaffId AND timing.ScheduleDate=dates.D AND timing.TenantId=@TenantId
                    WHERE person.TenantId=@TenantId AND person.IsActive=1
                ), Missing AS(
                    SELECT TenantId,PersonId,D
                    FROM EffectiveDays dayInfo
                    WHERE dayInfo.IsOn=1
                      AND dayInfo.IsOpenAttendance=0
                      AND dayInfo.PriorMonthlyAbsentCount>=dayInfo.AdjustAbsentDays
                      AND dayInfo.AttendanceTypeCode<>N'NONE'
                      AND DATEADD(minute,dayInfo.AbsentAfterMinutes,
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
                    WHEN attendance.CheckInUtc IS NULL AND effective.PriorMonthlyAbsentCount>=effective.AdjustAbsentDays THEN @Absent
                    WHEN attendance.CheckInUtc IS NULL THEN attendance.AttendanceStatusId
                    WHEN attendance.CheckOutUtc IS NULL
                      AND DATEADD(minute,effective.MissingCheckoutAfterMinutes,
                          DATEADD(day,CASE WHEN effective.ShiftEnd<=effective.ShiftStart THEN 1 ELSE 0 END,
                              DATEADD(minute,DATEDIFF(minute,'00:00',effective.ShiftEnd),CONVERT(datetime2,attendance.AttendanceDate))))<=@NowLocal
                        THEN @Absent
                    WHEN attendance.CheckOutUtc IS NULL
                      AND CONVERT(time,(@AsOfUtc AT TIME ZONE 'UTC') AT TIME ZONE @TimeZoneId)>
                          CONVERT(time,DATEADD(minute,effective.CheckInAdjustMinutes,effective.ShiftStart)) THEN @Late
                    WHEN attendance.CheckOutUtc IS NULL THEN @Present
                    WHEN DATEDIFF(minute,attendance.CheckInUtc,attendance.CheckOutUtc)-attendance.TotalBreakMinutes
                      < COALESCE(NULLIF(timing.WorkingMinutes,0),
                          NULLIF(setting.WorkingMinutes,0),
                          (CASE WHEN DATEDIFF(minute,effective.ShiftStart,effective.ShiftEnd)>0
                                THEN DATEDIFF(minute,effective.ShiftStart,effective.ShiftEnd)
                                ELSE DATEDIFF(minute,effective.ShiftStart,effective.ShiftEnd)+1440 END)) - effective.CheckOutAdjustMinutes
                        THEN @EarlyDeparture
                    WHEN CONVERT(time,(attendance.CheckInUtc AT TIME ZONE 'UTC') AT TIME ZONE @TimeZoneId)>
                          CONVERT(time,DATEADD(minute,effective.CheckInAdjustMinutes,effective.ShiftStart)) THEN @CompletedLate
                    ELSE @Present END,
                    ModifiedDate=@AsOfUtc
                FROM dbo.AttendanceRecords attendance
                JOIN dbo.Persons person ON person.PersonId=attendance.PersonId
                LEFT JOIN dbo.StaffVacancy staff ON staff.PersonId=person.PersonId
                LEFT JOIN dbo.AttendanceMapRules mapRule
                  ON mapRule.StaffId=staff.StaffId AND mapRule.TenantId=@TenantId
                LEFT JOIN dbo.AttendanceRuleSettings setting
                  ON setting.TenantId=@TenantId
                 AND setting.AttendanceEntryTypeId=mapRule.AttendanceEntryTypeId
                 AND setting.IsActive=1
                 AND setting.IsApproved=1
                LEFT JOIN dbo.EmployeeTimingSchedules timing
                  ON timing.StaffId=staff.StaffId
                 AND timing.ScheduleDate=attendance.AttendanceDate
                 AND timing.TenantId=@TenantId
                LEFT JOIN dbo.AppLookupValues timingHoliday
                  ON timingHoliday.LookupValueId=timing.HolidayTypeId
                CROSS APPLY(SELECT
                    COALESCE(timing.IsOn,CASE WHEN DATENAME(weekday,attendance.AttendanceDate) IN(N'Saturday',N'Sunday') THEN 0 ELSE 1 END) IsOn,
                    COALESCE(TRY_CONVERT(time(0),timing.TimeFrom),TRY_CONVERT(time(0),mapRule.TimeFrom),TRY_CONVERT(time(0),person.ShiftStartTime)) ShiftStart,
                    COALESCE(TRY_CONVERT(time(0),timing.TimeTo),TRY_CONVERT(time(0),mapRule.TimeTo),TRY_CONVERT(time(0),person.ShiftEndTime)) ShiftEnd,
                    COALESCE(setting.CheckInAdjustMinutes,@Grace) CheckInAdjustMinutes,
                    COALESCE(setting.CheckOutAdjustMinutes,@Tolerance) CheckOutAdjustMinutes,
                    COALESCE(setting.MissingCheckoutAfterShiftEndMinutes,@MissingOutAfter) MissingCheckoutAfterMinutes,
                    COALESCE(setting.AdjustAbsentDays,0) AdjustAbsentDays,
                    (
                        SELECT COUNT_BIG(1)
                        FROM dbo.AttendanceRecords monthlyAbsent
                        WHERE monthlyAbsent.TenantId=attendance.TenantId
                          AND monthlyAbsent.PersonId=attendance.PersonId
                          AND monthlyAbsent.AttendanceStatusId=@Absent
                          AND monthlyAbsent.AttendanceDate>=DATEFROMPARTS(YEAR(attendance.AttendanceDate),MONTH(attendance.AttendanceDate),1)
                          AND monthlyAbsent.AttendanceDate<attendance.AttendanceDate
                    ) PriorMonthlyAbsentCount
                ) effective
                WHERE attendance.TenantId=@TenantId
                  AND attendance.AttendanceDate BETWEEN @DateFrom AND @DateTo;

                ;WITH LockCandidates AS(
                    SELECT person.PersonId,person.IdentityUserId,setting.AccountLockAbsentDays
                    FROM dbo.Persons person
                    JOIN dbo.StaffVacancy staff ON staff.PersonId=person.PersonId
                    JOIN dbo.AttendanceMapRules mapRule
                      ON mapRule.StaffId=staff.StaffId AND mapRule.TenantId=@TenantId
                    JOIN dbo.AttendanceRuleSettings setting
                      ON setting.TenantId=@TenantId
                     AND setting.AttendanceEntryTypeId=mapRule.AttendanceEntryTypeId
                     AND setting.IsActive=1
                     AND setting.IsApproved=1
                    LEFT JOIN dbo.AspNetUsers identityUser
                      ON identityUser.Id=person.IdentityUserId
                    WHERE person.TenantId=@TenantId
                      AND person.IsActive=1
                      AND setting.AccountLockAbsentDays>0
                      AND ISNULL(identityUser.IsTenantAdmin,0)=0
                      AND ISNULL(identityUser.IsSuperAdmin,0)=0
                ), LockedPeople AS(
                    SELECT candidate.PersonId,candidate.IdentityUserId
                    FROM LockCandidates candidate
                    CROSS APPLY(
                        SELECT recentRows.AttendanceStatusId
                        FROM (
                            SELECT attendance.AttendanceStatusId,
                                   ROW_NUMBER() OVER(ORDER BY attendance.AttendanceDate DESC) RowNumber
                            FROM dbo.AttendanceRecords attendance
                            WHERE attendance.TenantId=@TenantId
                              AND attendance.PersonId=candidate.PersonId
                              AND attendance.AttendanceDate<=@DateTo
                        ) recentRows
                        WHERE recentRows.RowNumber<=candidate.AccountLockAbsentDays
                    ) recent
                    GROUP BY candidate.PersonId,candidate.IdentityUserId,candidate.AccountLockAbsentDays
                    HAVING COUNT_BIG(1)=candidate.AccountLockAbsentDays
                       AND SUM(CASE WHEN recent.AttendanceStatusId=@Absent THEN 1 ELSE 0 END)=candidate.AccountLockAbsentDays
                )
                UPDATE person
                   SET IsActive=0,
                       ModifiedDate=@AsOfUtc
                FROM dbo.Persons person
                JOIN LockedPeople locked
                  ON locked.PersonId=person.PersonId;

                UPDATE identityUser
                   SET LockoutEnabled=1,
                       LockoutEnd='9999-12-31T23:59:59.9999999+00:00'
                FROM dbo.AspNetUsers identityUser
                JOIN LockedPeople locked
                  ON locked.IdentityUserId=identityUser.Id
                WHERE locked.IdentityUserId IS NOT NULL;
            END
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP VIEW IF EXISTS dbo.vw_AttendanceRuleSettings;");
        migrationBuilder.Sql("DROP TABLE IF EXISTS dbo.AttendanceRuleSettings;");
    }
}
