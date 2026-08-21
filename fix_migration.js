const fs = require("fs");
const path = "Accounts/Migrations/20260821063512_AddProcessApprovalCode.cs";
let content = fs.readFileSync(path, "utf8");

const spSql = `
                migrationBuilder.Sql(@"
CREATE OR ALTER PROCEDURE [dbo].[usp_Attendance_DeductionReport]
    @TenantId INT,
    @Month INT,
    @Year INT,
    @VisiblePersonIds NVARCHAR(MAX) = NULL 
AS
BEGIN
    SET NOCOUNT ON;

    WITH VisiblePeople AS (
        SELECT CAST(value AS UNIQUEIDENTIFIER) AS PersonId
        FROM OPENJSON(@VisiblePersonIds)
    ),
    Dates AS (
        SELECT CAST(CAST(@Year AS VARCHAR) + '-' + RIGHT('0' + CAST(@Month AS VARCHAR), 2) + '-01' AS DATE) AS StartDate,
               EOMONTH(CAST(CAST(@Year AS VARCHAR) + '-' + RIGHT('0' + CAST(@Month AS VARCHAR), 2) + '-01' AS DATE)) AS EndDate
    ),
    StaffRows AS (
        SELECT 
            p.Id AS PersonId,
            v.Id AS StaffId,
            v.VacancyNumber AS StaffNumber,
            p.FirstName + ' ' + COALESCE(p.LastName, '') AS EmployeeName,
            jt.TitleName AS JobTitle,
            d.Name AS Department,
            s.CurrentPay,
            COALESCE(ars.IsOvertimeBonusActive, 0) AS IsOvertimeBonusActive
        FROM dbo.Persons p
        INNER JOIN dbo.StaffVacancies v ON v.PersonId = p.Id AND v.IsActive = 1
        LEFT JOIN dbo.JobTitles jt ON v.JobTitleId = jt.Id
        LEFT JOIN dbo.Departments d ON v.DepartmentId = d.Id
        LEFT JOIN dbo.StaffAssessments s ON s.VacancyId = v.Id AND s.IsActive = 1
        LEFT JOIN dbo.AttendanceRuleSettings ars ON ars.TenantId = @TenantId
        WHERE p.TenantId = @TenantId 
          AND (@VisiblePersonIds IS NULL OR p.Id IN (SELECT PersonId FROM VisiblePeople))
    ),
    BaseRows AS (
        SELECT 
            s.*,
            d.StartDate,
            d.EndDate,
            CASE 
                WHEN s.CurrentPay > 0 THEN s.CurrentPay / 30.0 
                ELSE 0 
            END AS PerDay,
            CASE 
                WHEN s.CurrentPay > 0 THEN (s.CurrentPay / 30.0) / 8.0 
                ELSE 0 
            END AS PerHour
        FROM StaffRows s
        CROSS JOIN Dates d
    ),
    AggregatedRows AS (
        SELECT 
            b.*,
            COUNT(DISTINCT a.AttendanceDate) AS MonthWorkingDays,
            COUNT(DISTINCT a.AttendanceDate) * 8 * 60 AS MonthWorkingMinutes,
            COALESCE(SUM(DATEDIFF(MINUTE, a.CheckInTime, COALESCE(a.CheckOutTime, a.CheckInTime))), 0) AS MonthAttendanceMinutes
        FROM BaseRows b
        LEFT JOIN dbo.AttendanceRecords a ON a.PersonId = b.PersonId 
                                          AND a.AttendanceDate >= b.StartDate 
                                          AND a.AttendanceDate <= b.EndDate
                                          AND a.StatusName NOT IN ('Day Off', 'Holiday')
        GROUP BY 
            b.PersonId, b.StaffId, b.StaffNumber, b.EmployeeName, b.JobTitle, b.Department, 
            b.CurrentPay, b.IsOvertimeBonusActive, b.StartDate, b.EndDate, b.PerDay, b.PerHour
    ),
    CalculatedRows AS (
        SELECT 
            agg.*,
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
        CAST(CASE WHEN c.IsOvertimeBonusActive = 1 THEN (c.NetOvertimeMinutes / 60.0) * c.PerHour ELSE 0 END AS decimal(18,2)) AS OvertimeBonusAmount,
        CAST(COALESCE(s.IsOvertimeApproved, 0) AS bit) AS IsOvertimeApproved,
        CAST(COALESCE(s.AdjustmentAmount, 0) AS decimal(18,2)) AS AdjustmentAmount,
        CAST(COALESCE(s.IsAdjustmentApproved, 0) AS bit) AS IsAdjustmentApproved,
        s.AdjustmentRemarks,
        CAST(c.CurrentPay - ((c.NetShortMinutes / 60.0) * c.PerHour) + (CASE WHEN s.IsOvertimeApproved = 1 AND c.IsOvertimeBonusActive = 1 THEN (c.NetOvertimeMinutes / 60.0) * c.PerHour ELSE 0 END) + (CASE WHEN s.IsAdjustmentApproved = 1 THEN COALESCE(s.AdjustmentAmount, 0) ELSE 0 END) AS decimal(18,2)) AS FinalSalary
    FROM CalculatedRows c
    LEFT JOIN dbo.AttendanceMonthlySettlements s ON s.PersonId = c.PersonId AND s.SettlementYear = @Year AND s.SettlementMonth = @Month AND s.TenantId = @TenantId
    ORDER BY c.EmployeeName;
END
");
`;

content = content.replace("protected override void Up(MigrationBuilder migrationBuilder)\r\n        {", "protected override void Up(MigrationBuilder migrationBuilder)\r\n        {" + spSql);
fs.writeFileSync(path, content, "utf8");
console.log("Done");

