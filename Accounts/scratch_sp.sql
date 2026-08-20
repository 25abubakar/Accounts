
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

