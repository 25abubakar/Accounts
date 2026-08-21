const fs = require("fs");
let sql = fs.readFileSync("original_sp.sql", "utf8");

const oldFirstSelect = `                          SELECT
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
                          WHERE 1 = 0;`;

const newFirstSelect = `                          SELECT
                              CAST(NULL AS bigint) AS Id,
                              CAST(NULL AS uniqueidentifier) AS PersonId,
                              CAST(NULL AS uniqueidentifier) AS StaffId,
                              CAST(N'' AS nvarchar(max)) AS StaffNumber,
                              CAST(N'' AS nvarchar(max)) AS EmployeeName,
                              CAST(N'' AS nvarchar(max)) AS JobTitle,
                              CAST(N'' AS nvarchar(max)) AS Department,
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
                              CAST(NULL AS decimal(18,2)) AS AdjustmentAmount,
                              CAST(NULL AS bit) AS IsAdjustmentApproved,
                              CAST(N'' AS nvarchar(255)) AS AdjustmentRemarks,
                              CAST(NULL AS decimal(18,2)) AS FinalSalary
                          WHERE 1 = 0;`;

// Replace first block
sql = sql.replace(oldFirstSelect, newFirstSelect);


const oldSelect = `                    SELECT
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
                END`;

const newSelect = `                    SELECT
                        ROW_NUMBER() OVER (ORDER BY monthly.EmployeeName, monthly.StaffNumber) AS Id,
                        monthly.PersonId,
                        monthly.StaffId,
                        monthly.StaffNumber,
                        monthly.EmployeeName,
                        monthly.JobTitle,
                        monthly.Department,
                        monthly.[Month],
                        monthly.[Year],
                        monthly.PerDay,
                        monthly.PerHour,
                        (monthly.TotalWorkingMinutes / (8*60)) AS MonthWorkingDays,
                        monthly.TotalWorkingMinutes AS MonthWorkingMinutes,
                        monthly.TotalAttendanceMinutes AS MonthAttendanceMinutes,
                        monthly.DeductionMinutes AS NetShortMinutes,
                        CASE WHEN monthly.TotalAttendanceMinutes > monthly.TotalWorkingMinutes 
                             THEN monthly.TotalAttendanceMinutes - monthly.TotalWorkingMinutes 
                             ELSE 0 END AS NetOvertimeMinutes,
                        monthly.NetDeduction,
                        -- Calculate bonus
                        CAST(CASE WHEN COALESCE((SELECT TOP 1 IsOvertimeBonusActive FROM dbo.AttendanceRuleSettings WHERE TenantId = @TenantId), 0) = 1 
                                  THEN (CASE WHEN monthly.TotalAttendanceMinutes > monthly.TotalWorkingMinutes THEN monthly.TotalAttendanceMinutes - monthly.TotalWorkingMinutes ELSE 0 END / 60.0) * monthly.PerHour 
                                  ELSE 0 END AS decimal(18,2)) AS OvertimeBonusAmount,
                        CAST(COALESCE(s.IsOvertimeApproved, 0) AS bit) AS IsOvertimeApproved,
                        CAST(COALESCE(s.AdjustmentAmount, 0) AS decimal(18,2)) AS AdjustmentAmount,
                        CAST(COALESCE(s.IsAdjustmentApproved, 0) AS bit) AS IsAdjustmentApproved,
                        CAST(s.AdjustmentRemarks AS nvarchar(255)) AS AdjustmentRemarks,
                        
                        -- Final Salary
                        CAST(
                            (SELECT TOP 1 COALESCE(hr.CurrentPay, hr.BasicSalary, 0) FROM dbo.PersonHrProfiles hr WHERE hr.PersonId = monthly.PersonId AND hr.TenantId = @TenantId)
                            - monthly.NetDeduction 
                            + (CASE WHEN s.IsOvertimeApproved = 1 AND COALESCE((SELECT TOP 1 IsOvertimeBonusActive FROM dbo.AttendanceRuleSettings WHERE TenantId = @TenantId), 0) = 1 THEN (CASE WHEN monthly.TotalAttendanceMinutes > monthly.TotalWorkingMinutes THEN monthly.TotalAttendanceMinutes - monthly.TotalWorkingMinutes ELSE 0 END / 60.0) * monthly.PerHour ELSE 0 END)
                            + (CASE WHEN s.IsAdjustmentApproved = 1 THEN COALESCE(s.AdjustmentAmount, 0) ELSE 0 END)
                        AS decimal(18,2)) AS FinalSalary

                    FROM AggregatedRows monthly
                    LEFT JOIN dbo.AttendanceMonthlySettlements s ON s.PersonId = monthly.PersonId AND s.SettlementYear = @Year AND s.SettlementMonth = @Month AND s.TenantId = @TenantId
                    ORDER BY monthly.EmployeeName, monthly.StaffNumber
                    OPTION (MAXRECURSION 31);
                END`;

// Replace final block
sql = sql.replace(oldSelect, newSelect);

if (!sql.includes("CREATE OR ALTER PROCEDURE")) {
    sql = sql.replace("CREATE PROCEDURE", "CREATE OR ALTER PROCEDURE");
}

fs.writeFileSync("update_sp_final6.sql", sql, "utf8");
console.log("Written update_sp_final6.sql");

