using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformStatusToAttendance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PlatformActionStatusId",
                table: "AttendanceRecords",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PlatformVerificationStatusId",
                table: "AttendanceRecords",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PlatformAbsentStatusId",
                table: "AttendancePolicies",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PlatformCompletedLateStatusId",
                table: "AttendancePolicies",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PlatformEarlyDepartureStatusId",
                table: "AttendancePolicies",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PlatformLateStatusId",
                table: "AttendancePolicies",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PlatformPresentStatusId",
                table: "AttendancePolicies",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PlatformShortLeaveStatusId",
                table: "AttendancePolicies",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRecords_PlatformActionStatusId",
                table: "AttendanceRecords",
                column: "PlatformActionStatusId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRecords_PlatformVerificationStatusId",
                table: "AttendanceRecords",
                column: "PlatformVerificationStatusId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendancePolicies_PlatformAbsentStatusId",
                table: "AttendancePolicies",
                column: "PlatformAbsentStatusId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendancePolicies_PlatformCompletedLateStatusId",
                table: "AttendancePolicies",
                column: "PlatformCompletedLateStatusId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendancePolicies_PlatformEarlyDepartureStatusId",
                table: "AttendancePolicies",
                column: "PlatformEarlyDepartureStatusId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendancePolicies_PlatformLateStatusId",
                table: "AttendancePolicies",
                column: "PlatformLateStatusId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendancePolicies_PlatformPresentStatusId",
                table: "AttendancePolicies",
                column: "PlatformPresentStatusId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendancePolicies_PlatformShortLeaveStatusId",
                table: "AttendancePolicies",
                column: "PlatformShortLeaveStatusId");

            migrationBuilder.AddForeignKey(
                name: "FK_AttendancePolicies_ActionStatuses_PlatformAbsentStatusId",
                table: "AttendancePolicies",
                column: "PlatformAbsentStatusId",
                principalSchema: "PlatformSettings",
                principalTable: "ActionStatuses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AttendancePolicies_ActionStatuses_PlatformCompletedLateStatusId",
                table: "AttendancePolicies",
                column: "PlatformCompletedLateStatusId",
                principalSchema: "PlatformSettings",
                principalTable: "ActionStatuses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AttendancePolicies_ActionStatuses_PlatformEarlyDepartureStatusId",
                table: "AttendancePolicies",
                column: "PlatformEarlyDepartureStatusId",
                principalSchema: "PlatformSettings",
                principalTable: "ActionStatuses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AttendancePolicies_ActionStatuses_PlatformLateStatusId",
                table: "AttendancePolicies",
                column: "PlatformLateStatusId",
                principalSchema: "PlatformSettings",
                principalTable: "ActionStatuses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AttendancePolicies_ActionStatuses_PlatformPresentStatusId",
                table: "AttendancePolicies",
                column: "PlatformPresentStatusId",
                principalSchema: "PlatformSettings",
                principalTable: "ActionStatuses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AttendancePolicies_ActionStatuses_PlatformShortLeaveStatusId",
                table: "AttendancePolicies",
                column: "PlatformShortLeaveStatusId",
                principalSchema: "PlatformSettings",
                principalTable: "ActionStatuses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AttendanceRecords_ActionStatuses_PlatformActionStatusId",
                table: "AttendanceRecords",
                column: "PlatformActionStatusId",
                principalSchema: "PlatformSettings",
                principalTable: "ActionStatuses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AttendanceRecords_ActionStatuses_PlatformVerificationStatusId",
                table: "AttendanceRecords",
                column: "PlatformVerificationStatusId",
                principalSchema: "PlatformSettings",
                principalTable: "ActionStatuses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql(@"
                UPDATE r
                SET PlatformActionStatusId = pas.Id
                FROM AttendanceRecords r
                INNER JOIN ProcessStatusStyles pss ON r.AttendanceStatusId = pss.Id
                INNER JOIN PlatformSettings.StatusCrDbValues crdb ON crdb.DbValue = pss.Code AND (crdb.TenantId = r.TenantId OR crdb.TenantId IS NULL)
                INNER JOIN PlatformSettings.ActionStatuses pas ON pas.StatusId = crdb.StatusId
                INNER JOIN PlatformSettings.Actions pa ON pa.Id = pas.ActionId
                WHERE pa.Name = N'Attendance' AND (pa.TenantId = r.TenantId OR pa.TenantId IS NULL);

                UPDATE r
                SET PlatformVerificationStatusId = pas.Id
                FROM AttendanceRecords r
                INNER JOIN ProcessStatusStyles pss ON r.VerificationStatusId = pss.Id
                INNER JOIN PlatformSettings.StatusCrDbValues crdb ON crdb.DbValue = pss.Code AND (crdb.TenantId = r.TenantId OR crdb.TenantId IS NULL)
                INNER JOIN PlatformSettings.ActionStatuses pas ON pas.StatusId = crdb.StatusId
                INNER JOIN PlatformSettings.Actions pa ON pa.Id = pas.ActionId
                WHERE pa.Name = N'Attendance' AND (pa.TenantId = r.TenantId OR pa.TenantId IS NULL);

                UPDATE p
                SET PlatformPresentStatusId = pas.Id
                FROM AttendancePolicies p
                INNER JOIN ProcessStatusStyles pss ON p.PresentStatusId = pss.Id
                INNER JOIN PlatformSettings.StatusCrDbValues crdb ON crdb.DbValue = pss.Code AND (crdb.TenantId = pss.TenantId OR crdb.TenantId IS NULL)
                INNER JOIN PlatformSettings.ActionStatuses pas ON pas.StatusId = crdb.StatusId
                INNER JOIN PlatformSettings.Actions pa ON pa.Id = pas.ActionId
                WHERE pa.Name = N'Attendance' AND (pa.TenantId = pss.TenantId OR pa.TenantId IS NULL);

                UPDATE p
                SET PlatformLateStatusId = pas.Id
                FROM AttendancePolicies p
                INNER JOIN ProcessStatusStyles pss ON p.LateStatusId = pss.Id
                INNER JOIN PlatformSettings.StatusCrDbValues crdb ON crdb.DbValue = pss.Code AND (crdb.TenantId = pss.TenantId OR crdb.TenantId IS NULL)
                INNER JOIN PlatformSettings.ActionStatuses pas ON pas.StatusId = crdb.StatusId
                INNER JOIN PlatformSettings.Actions pa ON pa.Id = pas.ActionId
                WHERE pa.Name = N'Attendance' AND (pa.TenantId = pss.TenantId OR pa.TenantId IS NULL);

                UPDATE p
                SET PlatformCompletedLateStatusId = pas.Id
                FROM AttendancePolicies p
                INNER JOIN ProcessStatusStyles pss ON p.CompletedLateStatusId = pss.Id
                INNER JOIN PlatformSettings.StatusCrDbValues crdb ON crdb.DbValue = pss.Code AND (crdb.TenantId = pss.TenantId OR crdb.TenantId IS NULL)
                INNER JOIN PlatformSettings.ActionStatuses pas ON pas.StatusId = crdb.StatusId
                INNER JOIN PlatformSettings.Actions pa ON pa.Id = pas.ActionId
                WHERE pa.Name = N'Attendance' AND (pa.TenantId = pss.TenantId OR pa.TenantId IS NULL);

                UPDATE p
                SET PlatformShortLeaveStatusId = pas.Id
                FROM AttendancePolicies p
                INNER JOIN ProcessStatusStyles pss ON p.ShortLeaveStatusId = pss.Id
                INNER JOIN PlatformSettings.StatusCrDbValues crdb ON crdb.DbValue = pss.Code AND (crdb.TenantId = pss.TenantId OR crdb.TenantId IS NULL)
                INNER JOIN PlatformSettings.ActionStatuses pas ON pas.StatusId = crdb.StatusId
                INNER JOIN PlatformSettings.Actions pa ON pa.Id = pas.ActionId
                WHERE pa.Name = N'Attendance' AND (pa.TenantId = pss.TenantId OR pa.TenantId IS NULL);

                UPDATE p
                SET PlatformEarlyDepartureStatusId = pas.Id
                FROM AttendancePolicies p
                INNER JOIN ProcessStatusStyles pss ON p.EarlyDepartureStatusId = pss.Id
                INNER JOIN PlatformSettings.StatusCrDbValues crdb ON crdb.DbValue = pss.Code AND (crdb.TenantId = pss.TenantId OR crdb.TenantId IS NULL)
                INNER JOIN PlatformSettings.ActionStatuses pas ON pas.StatusId = crdb.StatusId
                INNER JOIN PlatformSettings.Actions pa ON pa.Id = pas.ActionId
                WHERE pa.Name = N'Attendance' AND (pa.TenantId = pss.TenantId OR pa.TenantId IS NULL);

                UPDATE p
                SET PlatformAbsentStatusId = pas.Id
                FROM AttendancePolicies p
                INNER JOIN ProcessStatusStyles pss ON p.AbsentStatusId = pss.Id
                INNER JOIN PlatformSettings.StatusCrDbValues crdb ON crdb.DbValue = pss.Code AND (crdb.TenantId = pss.TenantId OR crdb.TenantId IS NULL)
                INNER JOIN PlatformSettings.ActionStatuses pas ON pas.StatusId = crdb.StatusId
                INNER JOIN PlatformSettings.Actions pa ON pa.Id = pas.ActionId
                WHERE pa.Name = N'Attendance' AND (pa.TenantId = pss.TenantId OR pa.TenantId IS NULL);
            ");

            migrationBuilder.Sql(
            """
            CREATE OR ALTER PROCEDURE dbo.usp_Attendance_EvaluateStatuses
                @TenantId int,
                @DateFrom date,
                @DateTo date,
                @AsOfUtc datetime2
            AS
            BEGIN
                SET NOCOUNT ON;
                SET XACT_ABORT ON;

                DECLARE
                    @PolicyId int,
                    @Grace int,
                    @AbsentAfter int,
                    @MissingOutAfter int,
                    @Tolerance int,
                    @Present int,
                    @PlatformPresent int,
                    @Late int,
                    @PlatformLate int,
                    @CompletedLate int,
                    @PlatformCompletedLate int,
                    @ShortLeave int,
                    @PlatformShortLeave int,
                    @EarlyDeparture int,
                    @PlatformEarlyDeparture int,
                    @Absent int,
                    @PlatformAbsent int,
                    @DayOff int,
                    @PlatformDayOff int,
                    @Holiday int,
                    @PlatformHoliday int,
                    @ProcessId int;

                SELECT TOP (1)
                    @PolicyId = Id,
                    @Grace = OnTimeGraceMinutesAfter,
                    @AbsentAfter = AbsentAfterShiftStartMinutes,
                    @MissingOutAfter = MissingCheckoutAfterShiftEndMinutes,
                    @Tolerance = FullDayToleranceMinutes,
                    @Present = PresentStatusId,
                    @PlatformPresent = PlatformPresentStatusId,
                    @Late = LateStatusId,
                    @PlatformLate = PlatformLateStatusId,
                    @CompletedLate = CompletedLateStatusId,
                    @PlatformCompletedLate = PlatformCompletedLateStatusId,
                    @ShortLeave = ShortLeaveStatusId,
                    @PlatformShortLeave = PlatformShortLeaveStatusId,
                    @EarlyDeparture = EarlyDepartureStatusId,
                    @PlatformEarlyDeparture = PlatformEarlyDepartureStatusId,
                    @Absent = AbsentStatusId,
                    @PlatformAbsent = PlatformAbsentStatusId
                FROM dbo.AttendancePolicies
                WHERE IsActive = 1
                  AND (TenantId = @TenantId OR TenantId IS NULL)
                ORDER BY CASE WHEN TenantId = @TenantId THEN 0 ELSE 1 END;

                IF @PolicyId IS NULL
                    THROW 51000, 'No active attendance policy is configured.', 1;

                SELECT @ProcessId = Id
                FROM dbo.Processes
                WHERE ProcessName = N'Attendance';

                SELECT TOP (1) @DayOff = pss.Id, @PlatformDayOff = pas.Id
                FROM dbo.ProcessStatusStyles pss
                INNER JOIN PlatformSettings.StatusCrDbValues crdb ON crdb.DbValue = pss.Code AND (crdb.TenantId = pss.TenantId OR crdb.TenantId IS NULL)
                INNER JOIN PlatformSettings.ActionStatuses pas ON pas.StatusId = crdb.StatusId
                INNER JOIN PlatformSettings.Actions pa ON pa.Id = pas.ActionId
                WHERE pss.ProcessId = @ProcessId
                  AND pa.Name = N'Attendance'
                  AND pss.Code = N'DO'
                  AND pss.IsActive = 1
                  AND (pss.TenantId = @TenantId OR pss.TenantId IS NULL)
                  AND (pa.TenantId = @TenantId OR pa.TenantId IS NULL)
                ORDER BY CASE WHEN pss.TenantId = @TenantId THEN 0 ELSE 1 END;

                SELECT TOP (1) @Holiday = pss.Id, @PlatformHoliday = pas.Id
                FROM dbo.ProcessStatusStyles pss
                INNER JOIN PlatformSettings.StatusCrDbValues crdb ON crdb.DbValue = pss.Code AND (crdb.TenantId = pss.TenantId OR crdb.TenantId IS NULL)
                INNER JOIN PlatformSettings.ActionStatuses pas ON pas.StatusId = crdb.StatusId
                INNER JOIN PlatformSettings.Actions pa ON pa.Id = pas.ActionId
                WHERE pss.ProcessId = @ProcessId
                  AND pa.Name = N'Attendance'
                  AND pss.Code = N'H'
                  AND pss.IsActive = 1
                  AND (pss.TenantId = @TenantId OR pss.TenantId IS NULL)
                  AND (pa.TenantId = @TenantId OR pa.TenantId IS NULL)
                ORDER BY CASE WHEN pss.TenantId = @TenantId THEN 0 ELSE 1 END;

                ;WITH Dates AS
                (
                    SELECT @DateFrom AS AttendanceDate
                    UNION ALL
                    SELECT DATEADD(day, 1, AttendanceDate)
                    FROM Dates
                    WHERE AttendanceDate < @DateTo
                ),
                EffectiveDays AS
                (
                    SELECT
                        person.TenantId,
                        person.PersonId,
                        dates.AttendanceDate,
                        ISNULL(mapRule.IsOpenAttendance, 0) AS IsOpenAttendance,
                        entryType.Code AS AttendanceTypeCode,
                        COALESCE(
                            timing.IsOn,
                            CASE WHEN DATENAME(weekday, dates.AttendanceDate) IN (N'Saturday', N'Sunday') THEN 0 ELSE 1 END
                        ) AS IsOn,
                        COALESCE(
                            TRY_CONVERT(time(0), timing.TimeFrom),
                            TRY_CONVERT(time(0), mapRule.TimeFrom),
                            TRY_CONVERT(time(0), person.ShiftStartTime)
                        ) AS ShiftStart,
                        COALESCE(setting.AbsentAfterShiftStartMinutes, @AbsentAfter) AS AbsentAfterMinutes,
                        COALESCE(setting.AdjustAbsentDays, 0) AS AdjustAbsentDays,
                        (
                            SELECT COUNT_BIG(1)
                            FROM dbo.AttendanceRecords previousAbsent
                            WHERE previousAbsent.TenantId = person.TenantId
                              AND previousAbsent.PersonId = person.PersonId
                              AND previousAbsent.AttendanceStatusId = @Absent
                              AND previousAbsent.AttendanceDate >= DATEFROMPARTS(YEAR(dates.AttendanceDate), MONTH(dates.AttendanceDate), 1)
                              AND previousAbsent.AttendanceDate < dates.AttendanceDate
                        ) AS PriorMonthlyAbsentCount
                    FROM dbo.Persons person
                    JOIN dbo.StaffVacancy staff
                      ON staff.PersonId = person.PersonId
                    JOIN dbo.AttendanceMapRules mapRule
                      ON mapRule.StaffId = staff.StaffId
                     AND mapRule.TenantId = @TenantId
                    JOIN dbo.AttendanceEntryTypes entryType
                      ON entryType.Id = mapRule.AttendanceEntryTypeId
                     AND entryType.IsActive = 1
                    CROSS JOIN Dates dates
                    LEFT JOIN dbo.AttendanceRuleSettings setting
                      ON setting.TenantId = @TenantId
                     AND setting.AttendanceEntryTypeId = mapRule.AttendanceEntryTypeId
                     AND setting.IsActive = 1
                     AND setting.IsApproved = 1
                    LEFT JOIN dbo.EmployeeTimingSchedules timing
                      ON timing.StaffId = staff.StaffId
                     AND timing.ScheduleDate = dates.AttendanceDate
                     AND timing.TenantId = @TenantId
                    WHERE person.TenantId = @TenantId
                      AND person.IsActive = 1
                ),
                Missing AS
                (
                    SELECT TenantId, PersonId, AttendanceDate
                    FROM EffectiveDays effective
                    WHERE effective.IsOn = 1
                      AND effective.IsOpenAttendance = 0
                      AND effective.AttendanceTypeCode <> N'NONE'
                      AND effective.PriorMonthlyAbsentCount >= effective.AdjustAbsentDays
                      AND @AsOfUtc > DATEADD(
                            minute,
                            effective.AbsentAfterMinutes,
                            DATEADD(
                                minute,
                                DATEDIFF(minute, CONVERT(time(0), '00:00'), effective.ShiftStart),
                                CONVERT(datetime2, effective.AttendanceDate)
                            )
                          )
                      AND NOT EXISTS
                      (
                          SELECT 1
                          FROM dbo.AttendanceRecords attendance
                          WHERE attendance.TenantId = effective.TenantId
                            AND attendance.PersonId = effective.PersonId
                            AND attendance.AttendanceDate = effective.AttendanceDate
                      )
                )
                INSERT dbo.AttendanceRecords
                    (TenantId, PersonId, AttendanceDate, AttendanceStatusId, PlatformActionStatusId, TotalBreakMinutes, CreatedDate, ModifiedDate)
                SELECT TenantId, PersonId, AttendanceDate, @Absent, @PlatformAbsent, 0, @AsOfUtc, @AsOfUtc
                FROM Missing
                OPTION (MAXRECURSION 367);

                UPDATE attendance
                   SET AttendanceStatusId =
                       CASE
                           WHEN effective.IsOn = 0 AND attendance.CheckInUtc IS NULL THEN
                               CASE
                                   WHEN timingHoliday.ValueCode IN (N'HOLIDAY', N'ANNUAL_HOLIDAY')
                                       THEN COALESCE(@Holiday, @DayOff, attendance.AttendanceStatusId)
                                   ELSE COALESCE(@DayOff, @Holiday, attendance.AttendanceStatusId)
                               END
                           WHEN ISNULL(mapRule.IsOpenAttendance, 0) = 1 AND attendance.CheckInUtc IS NOT NULL
                               THEN @Present
                           WHEN attendance.AttendanceStatusId = @ShortLeave
                               THEN @ShortLeave
                           WHEN attendance.CheckInUtc IS NULL
                                AND @AsOfUtc > effective.CheckInAbsentDeadline
                                AND effective.PriorMonthlyAbsentCount >= effective.AdjustAbsentDays
                               THEN @Absent
                           WHEN attendance.CheckInUtc IS NULL
                               THEN attendance.AttendanceStatusId
                           WHEN attendance.CheckOutUtc IS NULL
                                AND @AsOfUtc > effective.MissingCheckoutDeadline
                               THEN @Absent
                           WHEN attendance.CheckOutUtc IS NULL
                                AND attendance.CheckInUtc > effective.OnTimeDeadline
                               THEN @Late
                           WHEN attendance.CheckOutUtc IS NULL
                               THEN @Present
                           WHEN attendance.CheckOutUtc < attendance.CheckInUtc
                               THEN @Absent
                           WHEN attendance.CheckOutUtc > effective.MissingCheckoutDeadline
                               THEN @Absent
                           WHEN DATEDIFF(minute, attendance.CheckOutUtc, effective.ShiftEndLocal)
                                > effective.EarlyCheckoutAbsentAfterMinutes
                               THEN @Absent
                           WHEN DATEDIFF(minute, attendance.CheckInUtc, attendance.CheckOutUtc)
                                - ISNULL(attendance.TotalBreakMinutes, 0)
                                < effective.RequiredWorkingMinutes - effective.CheckOutAdjustMinutes
                               THEN @EarlyDeparture
                           WHEN attendance.CheckInUtc > effective.OnTimeDeadline
                               THEN @CompletedLate
                           ELSE @Present
                       END,
                       PlatformActionStatusId =
                       CASE
                           WHEN effective.IsOn = 0 AND attendance.CheckInUtc IS NULL THEN
                               CASE
                                   WHEN timingHoliday.ValueCode IN (N'HOLIDAY', N'ANNUAL_HOLIDAY')
                                       THEN COALESCE(@PlatformHoliday, @PlatformDayOff, attendance.PlatformActionStatusId)
                                   ELSE COALESCE(@PlatformDayOff, @PlatformHoliday, attendance.PlatformActionStatusId)
                               END
                           WHEN ISNULL(mapRule.IsOpenAttendance, 0) = 1 AND attendance.CheckInUtc IS NOT NULL
                               THEN @PlatformPresent
                           WHEN attendance.AttendanceStatusId = @ShortLeave
                               THEN @PlatformShortLeave
                           WHEN attendance.CheckInUtc IS NULL
                                AND @AsOfUtc > effective.CheckInAbsentDeadline
                                AND effective.PriorMonthlyAbsentCount >= effective.AdjustAbsentDays
                               THEN @PlatformAbsent
                           WHEN attendance.CheckInUtc IS NULL
                               THEN attendance.PlatformActionStatusId
                           WHEN attendance.CheckOutUtc IS NULL
                                AND @AsOfUtc > effective.MissingCheckoutDeadline
                               THEN @PlatformAbsent
                           WHEN attendance.CheckOutUtc IS NULL
                                AND attendance.CheckInUtc > effective.OnTimeDeadline
                               THEN @PlatformLate
                           WHEN attendance.CheckOutUtc IS NULL
                               THEN @PlatformPresent
                           WHEN attendance.CheckOutUtc < attendance.CheckInUtc
                               THEN @PlatformAbsent
                           WHEN attendance.CheckOutUtc > effective.MissingCheckoutDeadline
                               THEN @PlatformAbsent
                           WHEN DATEDIFF(minute, attendance.CheckOutUtc, effective.ShiftEndLocal)
                                > effective.EarlyCheckoutAbsentAfterMinutes
                               THEN @PlatformAbsent
                           WHEN DATEDIFF(minute, attendance.CheckInUtc, attendance.CheckOutUtc)
                                - ISNULL(attendance.TotalBreakMinutes, 0)
                                < effective.RequiredWorkingMinutes - effective.CheckOutAdjustMinutes
                               THEN @PlatformEarlyDeparture
                           WHEN attendance.CheckInUtc > effective.OnTimeDeadline
                               THEN @PlatformCompletedLate
                           ELSE @PlatformPresent
                       END,
                       ModifiedDate = @AsOfUtc
                FROM dbo.AttendanceRecords attendance
                JOIN dbo.Persons person
                  ON person.PersonId = attendance.PersonId
                LEFT JOIN dbo.StaffVacancy staff
                  ON staff.PersonId = person.PersonId
                LEFT JOIN dbo.AttendanceMapRules mapRule
                  ON mapRule.StaffId = staff.StaffId
                 AND mapRule.TenantId = @TenantId
                LEFT JOIN dbo.AttendanceRuleSettings setting
                  ON setting.TenantId = @TenantId
                 AND setting.AttendanceEntryTypeId = mapRule.AttendanceEntryTypeId
                 AND setting.IsActive = 1
                 AND setting.IsApproved = 1
                LEFT JOIN dbo.EmployeeTimingSchedules timing
                  ON timing.StaffId = staff.StaffId
                 AND timing.ScheduleDate = attendance.AttendanceDate
                 AND timing.TenantId = @TenantId
                LEFT JOIN dbo.AppLookupValues timingHoliday
                  ON timingHoliday.LookupValueId = timing.HolidayTypeId
                CROSS APPLY
                (
                    SELECT
                        COALESCE(
                            TRY_CONVERT(time(0), timing.TimeFrom),
                            TRY_CONVERT(time(0), mapRule.TimeFrom),
                            TRY_CONVERT(time(0), person.ShiftStartTime)
                        ) AS ShiftStart,
                        COALESCE(
                            TRY_CONVERT(time(0), timing.TimeTo),
                            TRY_CONVERT(time(0), mapRule.TimeTo),
                            TRY_CONVERT(time(0), person.ShiftEndTime)
                        ) AS ShiftEnd,
                        COALESCE(
                            timing.IsOn,
                            CASE WHEN DATENAME(weekday, attendance.AttendanceDate) IN (N'Saturday', N'Sunday') THEN 0 ELSE 1 END
                        ) AS IsOn
                ) shiftData
                CROSS APPLY
                (
                    SELECT
                        DATEADD(
                            minute,
                            DATEDIFF(minute, CONVERT(time(0), '00:00'), shiftData.ShiftStart),
                            CONVERT(datetime2, attendance.AttendanceDate)
                        ) AS ShiftStartLocal,
                        DATEADD(
                            day,
                            CASE WHEN shiftData.ShiftEnd <= shiftData.ShiftStart THEN 1 ELSE 0 END,
                            DATEADD(
                                minute,
                                DATEDIFF(minute, CONVERT(time(0), '00:00'), shiftData.ShiftEnd),
                                CONVERT(datetime2, attendance.AttendanceDate)
                            )
                        ) AS ShiftEndLocal
                ) windows
                CROSS APPLY
                (
                    SELECT
                        shiftData.IsOn,
                        windows.ShiftStartLocal,
                        windows.ShiftEndLocal,
                        DATEADD(minute, COALESCE(setting.CheckInAdjustMinutes, @Grace), windows.ShiftStartLocal) AS OnTimeDeadline,
                        DATEADD(minute, COALESCE(setting.AbsentAfterShiftStartMinutes, @AbsentAfter), windows.ShiftStartLocal) AS CheckInAbsentDeadline,
                        DATEADD(minute, COALESCE(setting.MissingCheckoutAfterShiftEndMinutes, @MissingOutAfter), windows.ShiftEndLocal) AS MissingCheckoutDeadline,
                        COALESCE(setting.EarlyCheckoutAbsentAfterMinutes, @MissingOutAfter) AS EarlyCheckoutAbsentAfterMinutes,
                        COALESCE(setting.CheckOutAdjustMinutes, @Tolerance) AS CheckOutAdjustMinutes,
                        COALESCE(
                            NULLIF(timing.WorkingMinutes, 0),
                            NULLIF(setting.WorkingMinutes, 0),
                            DATEDIFF(minute, windows.ShiftStartLocal, windows.ShiftEndLocal)
                        ) AS RequiredWorkingMinutes,
                        COALESCE(setting.AdjustAbsentDays, 0) AS AdjustAbsentDays,
                        (
                            SELECT COUNT_BIG(1)
                            FROM dbo.AttendanceRecords previousAbsent
                            WHERE previousAbsent.TenantId = attendance.TenantId
                              AND previousAbsent.PersonId = attendance.PersonId
                              AND previousAbsent.AttendanceStatusId = @Absent
                              AND previousAbsent.AttendanceDate >= DATEFROMPARTS(YEAR(attendance.AttendanceDate), MONTH(attendance.AttendanceDate), 1)
                              AND previousAbsent.AttendanceDate < attendance.AttendanceDate
                        ) AS PriorMonthlyAbsentCount
                ) effective
                WHERE attendance.TenantId = @TenantId
                  AND attendance.AttendanceDate BETWEEN @DateFrom AND @DateTo;
            END;
            """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AttendancePolicies_ActionStatuses_PlatformAbsentStatusId",
                table: "AttendancePolicies");

            migrationBuilder.DropForeignKey(
                name: "FK_AttendancePolicies_ActionStatuses_PlatformCompletedLateStatusId",
                table: "AttendancePolicies");

            migrationBuilder.DropForeignKey(
                name: "FK_AttendancePolicies_ActionStatuses_PlatformEarlyDepartureStatusId",
                table: "AttendancePolicies");

            migrationBuilder.DropForeignKey(
                name: "FK_AttendancePolicies_ActionStatuses_PlatformLateStatusId",
                table: "AttendancePolicies");

            migrationBuilder.DropForeignKey(
                name: "FK_AttendancePolicies_ActionStatuses_PlatformPresentStatusId",
                table: "AttendancePolicies");

            migrationBuilder.DropForeignKey(
                name: "FK_AttendancePolicies_ActionStatuses_PlatformShortLeaveStatusId",
                table: "AttendancePolicies");

            migrationBuilder.DropForeignKey(
                name: "FK_AttendanceRecords_ActionStatuses_PlatformActionStatusId",
                table: "AttendanceRecords");

            migrationBuilder.DropForeignKey(
                name: "FK_AttendanceRecords_ActionStatuses_PlatformVerificationStatusId",
                table: "AttendanceRecords");

            migrationBuilder.DropIndex(
                name: "IX_AttendanceRecords_PlatformActionStatusId",
                table: "AttendanceRecords");

            migrationBuilder.DropIndex(
                name: "IX_AttendanceRecords_PlatformVerificationStatusId",
                table: "AttendanceRecords");

            migrationBuilder.DropIndex(
                name: "IX_AttendancePolicies_PlatformAbsentStatusId",
                table: "AttendancePolicies");

            migrationBuilder.DropIndex(
                name: "IX_AttendancePolicies_PlatformCompletedLateStatusId",
                table: "AttendancePolicies");

            migrationBuilder.DropIndex(
                name: "IX_AttendancePolicies_PlatformEarlyDepartureStatusId",
                table: "AttendancePolicies");

            migrationBuilder.DropIndex(
                name: "IX_AttendancePolicies_PlatformLateStatusId",
                table: "AttendancePolicies");

            migrationBuilder.DropIndex(
                name: "IX_AttendancePolicies_PlatformPresentStatusId",
                table: "AttendancePolicies");

            migrationBuilder.DropIndex(
                name: "IX_AttendancePolicies_PlatformShortLeaveStatusId",
                table: "AttendancePolicies");

            migrationBuilder.DropColumn(
                name: "PlatformActionStatusId",
                table: "AttendanceRecords");

            migrationBuilder.DropColumn(
                name: "PlatformVerificationStatusId",
                table: "AttendanceRecords");

            migrationBuilder.DropColumn(
                name: "PlatformAbsentStatusId",
                table: "AttendancePolicies");

            migrationBuilder.DropColumn(
                name: "PlatformCompletedLateStatusId",
                table: "AttendancePolicies");

            migrationBuilder.DropColumn(
                name: "PlatformEarlyDepartureStatusId",
                table: "AttendancePolicies");

            migrationBuilder.DropColumn(
                name: "PlatformLateStatusId",
                table: "AttendancePolicies");

            migrationBuilder.DropColumn(
                name: "PlatformPresentStatusId",
                table: "AttendancePolicies");

            migrationBuilder.DropColumn(
                name: "PlatformShortLeaveStatusId",
                table: "AttendancePolicies");
        }
    }
}
