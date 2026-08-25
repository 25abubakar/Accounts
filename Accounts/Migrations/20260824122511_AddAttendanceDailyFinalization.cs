using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendanceDailyFinalization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AttendanceDailyFinalizations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StaffId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttendanceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    AttendanceRecordId = table.Column<long>(type: "bigint", nullable: true),
                    State = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    IsWorkingDay = table.Column<bool>(type: "bit", nullable: false),
                    IsFinalized = table.Column<bool>(type: "bit", nullable: false),
                    IsFullDayAbsent = table.Column<bool>(type: "bit", nullable: false),
                    RequiredMinutes = table.Column<int>(type: "int", nullable: false),
                    WorkedMinutes = table.Column<int>(type: "int", nullable: false),
                    ShortMinutes = table.Column<int>(type: "int", nullable: false),
                    OvertimeMinutes = table.Column<int>(type: "int", nullable: false),
                    FinalizedDateUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastEvaluatedDateUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceDailyFinalizations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttendanceDailyFinalizations_AttendanceRecords_AttendanceRecordId",
                        column: x => x.AttendanceRecordId,
                        principalTable: "AttendanceRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AttendanceDailyFinalizations_Persons_PersonId",
                        column: x => x.PersonId,
                        principalTable: "Persons",
                        principalColumn: "PersonId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttendanceDailyFinalizations_StaffVacancy_StaffId",
                        column: x => x.StaffId,
                        principalTable: "StaffVacancy",
                        principalColumn: "StaffId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceDailyFinalizations_AttendanceRecordId",
                table: "AttendanceDailyFinalizations",
                column: "AttendanceRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceDailyFinalizations_PersonId",
                table: "AttendanceDailyFinalizations",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceDailyFinalizations_StaffId",
                table: "AttendanceDailyFinalizations",
                column: "StaffId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceDailyFinalizations_TenantId_AttendanceDate_IsFinalized",
                table: "AttendanceDailyFinalizations",
                columns: new[] { "TenantId", "AttendanceDate", "IsFinalized" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceDailyFinalizations_TenantId_PersonId_AttendanceDate",
                table: "AttendanceDailyFinalizations",
                columns: new[] { "TenantId", "PersonId", "AttendanceDate" },
                unique: true);

            migrationBuilder.Sql(
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

                    ;WITH Dates AS
                    (
                        SELECT @DateFrom AS AttendanceDate
                        UNION ALL
                        SELECT DATEADD(day, 1, AttendanceDate)
                        FROM Dates
                        WHERE AttendanceDate < @DateTo
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
                            CAST(COALESCE(NULLIF(hr.BasicSalary, 0), hr.CurrentPay, 0) AS decimal(18,2)) AS Salary,
                            CAST(COALESCE(ruleSetting.IsOvertimeBonusActive, 0) AS bit) AS IsOvertimeBonusActive,
                            COALESCE(ruleSetting.AdjustAbsentDays, 0) AS AdjustAbsentDays,
                            ruleSetting.WorkingMinutes AS RuleWorkingMinutes,
                            mapRule.TimeFrom AS RuleTimeFrom,
                            mapRule.TimeTo AS RuleTimeTo,
                            UPPER(REPLACE(REPLACE(COALESCE(entryType.Code, N''), N'_', N''), N' ', N'')) AS EntryTypeCode,
                            CAST(hr.JoiningDate AS date) AS JoiningDate,
                            CAST(person.TerminationDateUtc AS date) AS TerminationDate
                        FROM VisiblePeople visible
                        JOIN dbo.Persons person
                          ON person.PersonId = visible.PersonId
                         AND person.TenantId = @TenantId
                         AND person.IsActive = 1
                        JOIN dbo.StaffVacancy staff
                          ON staff.PersonId = person.PersonId
                         AND staff.TenantId = @TenantId
                        LEFT JOIN dbo.Vacancies vacancy ON vacancy.VacancyId = staff.VacancyId
                        LEFT JOIN dbo.JobTitles jobTitle ON jobTitle.Id = vacancy.JobTitleId
                        LEFT JOIN dbo.OrganizationTree organization ON organization.Id = vacancy.OrganizationId
                        LEFT JOIN dbo.PersonHrProfiles hr
                          ON hr.PersonId = person.PersonId
                         AND hr.TenantId = @TenantId
                        LEFT JOIN dbo.AttendanceMapRules mapRule
                          ON mapRule.StaffId = staff.StaffId
                         AND mapRule.TenantId = @TenantId
                        LEFT JOIN PlatformTypes.AttendanceTypes entryType
                          ON entryType.Id = mapRule.AttendanceEntryTypeId
                         AND entryType.TenantId = @TenantId
                        OUTER APPLY
                        (
                            SELECT TOP (1)
                                setting.WorkingMinutes,
                                setting.AdjustAbsentDays,
                                setting.IsOvertimeBonusActive
                            FROM dbo.AttendanceRuleSettings setting
                            WHERE setting.TenantId = @TenantId
                              AND setting.AttendanceEntryTypeId = mapRule.AttendanceEntryTypeId
                              AND setting.IsActive = 1
                              AND setting.IsApproved = 1
                            ORDER BY setting.Id DESC
                        ) ruleSetting
                    ),
                    CalendarRows AS
                    (
                        SELECT
                            staff.PersonId,
                            dates.AttendanceDate,
                            CASE
                                WHEN staff.EntryTypeCode IN (N'NONE', N'NOTREQUIRED', N'NOREQUIREDATTENDANCE') THEN 0
                                WHEN dates.AttendanceDate < COALESCE(staff.JoiningDate, @DateFrom) THEN 0
                                WHEN staff.TerminationDate IS NOT NULL AND dates.AttendanceDate > staff.TerminationDate THEN 0
                                WHEN COALESCE(schedule.IsOn,
                                    CASE WHEN ((DATEDIFF(day, '19000101', dates.AttendanceDate) % 7 + 7) % 7) IN (5, 6)
                                         THEN 0 ELSE 1 END) = 0 THEN 0
                                WHEN COALESCE(schedule.WorkingMinutes, 0) > 0 THEN schedule.WorkingMinutes
                                WHEN COALESCE(staff.RuleWorkingMinutes, 0) > 0 THEN staff.RuleWorkingMinutes
                                ELSE
                                    CASE
                                        WHEN TRY_CONVERT(time(0), COALESCE(schedule.TimeTo, staff.RuleTimeTo, staff.ShiftEndTime)) >=
                                             TRY_CONVERT(time(0), COALESCE(schedule.TimeFrom, staff.RuleTimeFrom, staff.ShiftStartTime))
                                            THEN DATEDIFF(
                                                minute,
                                                TRY_CONVERT(time(0), COALESCE(schedule.TimeFrom, staff.RuleTimeFrom, staff.ShiftStartTime)),
                                                TRY_CONVERT(time(0), COALESCE(schedule.TimeTo, staff.RuleTimeTo, staff.ShiftEndTime)))
                                        ELSE 1440 - DATEDIFF(
                                                minute,
                                                TRY_CONVERT(time(0), COALESCE(schedule.TimeTo, staff.RuleTimeTo, staff.ShiftEndTime)),
                                                TRY_CONVERT(time(0), COALESCE(schedule.TimeFrom, staff.RuleTimeFrom, staff.ShiftStartTime)))
                                    END
                            END AS RequiredMinutes
                        FROM StaffRows staff
                        CROSS JOIN Dates dates
                        LEFT JOIN dbo.EmployeeTimingSchedules schedule
                          ON schedule.StaffId = staff.StaffId
                         AND schedule.ScheduleDate = dates.AttendanceDate
                         AND schedule.TenantId = @TenantId
                    ),
                    FullMonthRates AS
                    (
                        SELECT
                            calendar.PersonId,
                            SUM(CASE WHEN calendar.RequiredMinutes > 0 THEN 1 ELSE 0 END) AS FullMonthWorkingDays,
                            SUM(calendar.RequiredMinutes) AS FullMonthWorkingMinutes
                        FROM CalendarRows calendar
                        GROUP BY calendar.PersonId
                    ),
                    RankedFinalizations AS
                    (
                        SELECT
                            finalization.*,
                            SUM(CASE WHEN finalization.IsFinalized = 1 AND finalization.IsFullDayAbsent = 1 THEN 1 ELSE 0 END)
                                OVER (
                                    PARTITION BY finalization.PersonId
                                    ORDER BY finalization.AttendanceDate, finalization.Id
                                    ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS AbsentRank
                        FROM dbo.AttendanceDailyFinalizations finalization
                        JOIN VisiblePeople visible ON visible.PersonId = finalization.PersonId
                        WHERE finalization.TenantId = @TenantId
                          AND finalization.AttendanceDate BETWEEN @DateFrom AND @DateTo
                    ),
                    PeriodTotals AS
                    (
                        SELECT
                            staff.PersonId,
                            SUM(CASE WHEN finalization.IsFinalized = 1 AND finalization.IsWorkingDay = 1 THEN 1 ELSE 0 END) AS FinalizedWorkingDays,
                            SUM(CASE WHEN finalization.IsFinalized = 1 THEN finalization.RequiredMinutes ELSE 0 END) AS FinalizedRequiredMinutes,
                            SUM(CASE WHEN finalization.IsFinalized = 1 THEN finalization.WorkedMinutes ELSE 0 END) AS FinalizedWorkedMinutes,
                            SUM(CASE
                                WHEN finalization.IsFinalized <> 1 THEN 0
                                WHEN finalization.IsFullDayAbsent = 1 AND finalization.AbsentRank <= staff.AdjustAbsentDays THEN 0
                                ELSE finalization.ShortMinutes
                            END) AS DeductibleShortMinutes,
                            SUM(CASE WHEN finalization.IsFinalized = 1 THEN finalization.OvertimeMinutes ELSE 0 END) AS FinalizedOvertimeMinutes,
                            SUM(CASE WHEN finalization.State IN (N'OPEN', N'IN_PROGRESS') THEN 1 ELSE 0 END) AS OpenDays,
                            SUM(CASE WHEN finalization.State = N'PENDING_REVIEW' THEN 1 ELSE 0 END) AS PendingReviewDays,
                            MAX(CASE WHEN finalization.IsFinalized = 1 THEN finalization.AttendanceDate END) AS LastFinalizedDate
                        FROM StaffRows staff
                        LEFT JOIN RankedFinalizations finalization ON finalization.PersonId = staff.PersonId
                        GROUP BY staff.PersonId, staff.AdjustAbsentDays
                    ),
                    Calculated AS
                    (
                        SELECT
                            staff.*,
                            COALESCE(period.FinalizedWorkingDays, 0) AS FinalizedWorkingDays,
                            COALESCE(period.FinalizedRequiredMinutes, 0) AS FinalizedRequiredMinutes,
                            COALESCE(period.FinalizedWorkedMinutes, 0) AS FinalizedWorkedMinutes,
                            COALESCE(period.DeductibleShortMinutes, 0) AS DeductibleShortMinutes,
                            COALESCE(period.FinalizedOvertimeMinutes, 0) AS FinalizedOvertimeMinutes,
                            COALESCE(period.OpenDays, 0) AS OpenDays,
                            COALESCE(period.PendingReviewDays, 0) AS PendingReviewDays,
                            period.LastFinalizedDate,
                            CAST(CASE
                                WHEN COALESCE(rate.FullMonthWorkingDays, 0) > 0 AND staff.Salary > 0
                                    THEN staff.Salary / rate.FullMonthWorkingDays
                                ELSE 0 END AS decimal(18,2)) AS PerDay,
                            CAST(CASE
                                WHEN COALESCE(rate.FullMonthWorkingMinutes, 0) > 0 AND staff.Salary > 0
                                    THEN (staff.Salary / rate.FullMonthWorkingMinutes) * 60.0
                                ELSE 0 END AS decimal(18,2)) AS PerHour
                        FROM StaffRows staff
                        LEFT JOIN FullMonthRates rate ON rate.PersonId = staff.PersonId
                        LEFT JOIN PeriodTotals period ON period.PersonId = staff.PersonId
                    )
                    SELECT
                        CAST(ROW_NUMBER() OVER (ORDER BY calculated.EmployeeName, calculated.StaffNumber) AS bigint) AS Id,
                        calculated.PersonId,
                        calculated.StaffId,
                        calculated.StaffNumber,
                        calculated.EmployeeName,
                        calculated.JobTitle,
                        calculated.Department,
                        @Month AS [Month],
                        @Year AS [Year],
                        calculated.PerDay,
                        calculated.PerHour,
                        calculated.FinalizedWorkingDays AS MonthWorkingDays,
                        calculated.FinalizedRequiredMinutes AS MonthWorkingMinutes,
                        calculated.FinalizedWorkedMinutes AS MonthAttendanceMinutes,
                        calculated.DeductibleShortMinutes AS NetShortMinutes,
                        calculated.FinalizedOvertimeMinutes AS NetOvertimeMinutes,
                        CAST((calculated.DeductibleShortMinutes / 60.0) * calculated.PerHour AS decimal(18,2)) AS NetDeduction,
                        CAST(CASE WHEN calculated.IsOvertimeBonusActive = 1
                            THEN (calculated.FinalizedOvertimeMinutes / 60.0) * calculated.PerHour
                            ELSE 0 END AS decimal(18,2)) AS OvertimeBonusAmount,
                        CAST(COALESCE(settlement.IsOvertimeApproved, 0) AS bit) AS IsOvertimeApproved,
                        calculated.IsOvertimeBonusActive,
                        CAST(COALESCE(settlement.AdjustmentAmount, 0) AS decimal(18,2)) AS AdjustmentAmount,
                        CAST(COALESCE(settlement.IsAdjustmentApproved, 0) AS bit) AS IsAdjustmentApproved,
                        CAST(settlement.AdjustmentRemarks AS nvarchar(255)) AS AdjustmentRemarks,
                        CAST(
                            calculated.Salary
                            - ((calculated.DeductibleShortMinutes / 60.0) * calculated.PerHour)
                            + CASE WHEN settlement.IsOvertimeApproved = 1 AND calculated.IsOvertimeBonusActive = 1
                                THEN (calculated.FinalizedOvertimeMinutes / 60.0) * calculated.PerHour ELSE 0 END
                            + CASE WHEN settlement.IsAdjustmentApproved = 1
                                THEN COALESCE(settlement.AdjustmentAmount, 0) ELSE 0 END
                            AS decimal(18,2)) AS FinalSalary,
                        calculated.PendingReviewDays,
                        calculated.OpenDays,
                        calculated.LastFinalizedDate
                    FROM Calculated calculated
                    LEFT JOIN dbo.AttendanceMonthlySettlements settlement
                      ON settlement.PersonId = calculated.PersonId
                     AND settlement.SettlementYear = @Year
                     AND settlement.SettlementMonth = @Month
                     AND settlement.TenantId = @TenantId
                    ORDER BY calculated.EmployeeName, calculated.StaffNumber
                    OPTION (MAXRECURSION 366);
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.usp_Attendance_DeductionReport;");

            migrationBuilder.DropTable(
                name: "AttendanceDailyFinalizations");
        }
    }
}
