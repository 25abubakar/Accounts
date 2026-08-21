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
                END
                