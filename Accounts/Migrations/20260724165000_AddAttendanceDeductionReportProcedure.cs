using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    public partial class AddAttendanceDeductionReportProcedure : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
                            mapRule.AttendanceEntryTypeId,
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
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.usp_Attendance_DeductionReport;");
        }
    }
}
