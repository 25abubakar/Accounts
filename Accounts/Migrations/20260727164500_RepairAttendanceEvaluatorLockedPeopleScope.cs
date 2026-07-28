using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260727164500_RepairAttendanceEvaluatorLockedPeopleScope")]
public sealed class RepairAttendanceEvaluatorLockedPeopleScope : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'dbo.AttendanceRuleSettings', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.AttendanceRuleSettings', N'EarlyCheckoutAbsentAfterMinutes') IS NULL
            BEGIN
                ALTER TABLE dbo.AttendanceRuleSettings ADD EarlyCheckoutAbsentAfterMinutes int NOT NULL
                    CONSTRAINT DF_AttendanceRuleSettings_EarlyCheckoutAbsentAfterMinutes DEFAULT(120);
            END;
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
                    WHEN DATEDIFF(minute,effective.CheckOutLocal, effective.ShiftEndLocal) > effective.EarlyCheckoutAbsentAfterMinutes
                        THEN @Absent
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
                    COALESCE(setting.EarlyCheckoutAbsentAfterMinutes,@MissingOutAfter) EarlyCheckoutAbsentAfterMinutes,
                    COALESCE(setting.MissingCheckoutAfterShiftEndMinutes,@MissingOutAfter) MissingCheckoutAfterMinutes,
                    COALESCE(setting.AdjustAbsentDays,0) AdjustAbsentDays,
                    CONVERT(datetime2,(attendance.CheckOutUtc AT TIME ZONE 'UTC') AT TIME ZONE @TimeZoneId) CheckOutLocal,
                    DATEADD(day,CASE WHEN COALESCE(TRY_CONVERT(time(0),timing.TimeTo),TRY_CONVERT(time(0),mapRule.TimeTo),TRY_CONVERT(time(0),person.ShiftEndTime))<=COALESCE(TRY_CONVERT(time(0),timing.TimeFrom),TRY_CONVERT(time(0),mapRule.TimeFrom),TRY_CONVERT(time(0),person.ShiftStartTime)) THEN 1 ELSE 0 END,
                        DATEADD(minute,DATEDIFF(minute,'00:00',COALESCE(TRY_CONVERT(time(0),timing.TimeTo),TRY_CONVERT(time(0),mapRule.TimeTo),TRY_CONVERT(time(0),person.ShiftEndTime))),CONVERT(datetime2,attendance.AttendanceDate))) ShiftEndLocal,
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

                DECLARE @LockedPeople table
                (
                    PersonId uniqueidentifier NOT NULL PRIMARY KEY,
                    IdentityUserId nvarchar(450) NULL
                );

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
                      AND 1=0 -- Attendance reports must not directly lock application login accounts.
                      AND setting.AccountLockAbsentDays>0
                      AND ISNULL(identityUser.IsTenantAdmin,0)=0
                      AND ISNULL(identityUser.IsSuperAdmin,0)=0
                      AND NOT EXISTS (
                          SELECT 1
                          FROM dbo.AspNetUserRoles userRole
                          JOIN dbo.AspNetRoles role ON role.Id=userRole.RoleId
                          WHERE userRole.UserId=identityUser.Id
                            AND role.Name IN (N'SuperAdmin',N'Admin',N'TenantAdmin')
                      )
                )
                INSERT @LockedPeople(PersonId,IdentityUserId)
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
                       AND SUM(CASE WHEN recent.AttendanceStatusId=@Absent THEN 1 ELSE 0 END)=candidate.AccountLockAbsentDays;

                UPDATE person
                   SET IsActive=0
                FROM dbo.Persons person
                JOIN @LockedPeople locked
                  ON locked.PersonId=person.PersonId;

                UPDATE identityUser
                   SET LockoutEnabled=1,
                       LockoutEnd='9999-12-31T23:59:59.9999999+00:00'
                FROM dbo.AspNetUsers identityUser
                JOIN @LockedPeople locked
                  ON locked.IdentityUserId=identityUser.Id
                WHERE locked.IdentityUserId IS NOT NULL;
            END
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}

