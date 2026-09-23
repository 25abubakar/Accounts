CREATE OR ALTER PROCEDURE dbo.usp_Payroll_RecalculateRunTotals
    @TenantId int,
    @PayrollRunId bigint
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.PayrollRuns
        WHERE Id = @PayrollRunId
          AND TenantId = @TenantId
    )
        THROW 51000, 'Payroll run was not found in the active tenant.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM dbo.PayrollRuns
        WHERE Id = @PayrollRunId
          AND TenantId = @TenantId
          AND UPPER(Status) <> N'DRAFT'
    )
        THROW 51001, 'Only a Draft payroll run can be recalculated.', 1;

    ;WITH Totals AS
    (
        SELECT
            line.Id,
            CAST(ROUND(
                COALESCE(line.BasicSalary, 0)
              + COALESCE(line.AllowanceAmount, 0)
              + COALESCE(line.SalaryAdjustment, 0)
              + COALESCE(line.AssessmentAmount, 0)
              + COALESCE(line.BonusAmount, 0)
              + COALESCE(line.OvertimeAmount, 0), 2
            ) AS decimal(18,2)) AS GrossPay,
            CAST(ROUND(
                COALESCE(line.AttendanceDeduction, 0)
              - CASE
                    WHEN COALESCE(line.IsAttendanceAdjustmentApproved, 0) = 1
                     AND COALESCE(line.AttendanceAdjustment, 0) > 0
                    THEN CASE
                        WHEN line.AttendanceAdjustment > line.AttendanceDeduction
                            THEN line.AttendanceDeduction
                        ELSE line.AttendanceAdjustment
                    END
                    ELSE 0
                END
              + CASE
                    WHEN COALESCE(line.IsAttendanceAdjustmentApproved, 0) = 1
                     AND COALESCE(line.AttendanceAdjustment, 0) < 0
                    THEN -line.AttendanceAdjustment
                    ELSE 0
                END
              + COALESCE(line.StaffBenefitDeduction, 0)
              + COALESCE(line.TaxAmount, 0)
              + COALESCE(line.EmployeeEobiAmount, 0)
              + COALESCE(line.OtherDeduction, 0), 2
            ) AS decimal(18,2)) AS TotalDeduction
        FROM dbo.PayrollLines line
        INNER JOIN dbo.PayrollRuns run
            ON run.Id = line.PayrollRunId
           AND run.TenantId = line.TenantId
        WHERE line.TenantId = @TenantId
          AND line.PayrollRunId = @PayrollRunId
          AND UPPER(run.Status) = N'DRAFT'
    )
    UPDATE line
    SET
        line.TaxableIncome = totals.GrossPay,
        line.GrossPay = totals.GrossPay,
        line.TotalDeduction = totals.TotalDeduction,
        line.NetPay = CASE
            WHEN totals.GrossPay - totals.TotalDeduction > 0
                THEN CAST(totals.GrossPay - totals.TotalDeduction AS decimal(18,2))
            ELSE CAST(0 AS decimal(18,2))
        END,
        line.UpdatedOnUtc = SYSUTCDATETIME()
    FROM dbo.PayrollLines line
    INNER JOIN Totals totals ON totals.Id = line.Id;
END;

