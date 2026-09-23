-- Bonus generate candidates: multi-scale CSV, Min_Salary, Min_Service months, installment window.
-- Month on distribution = schedule start month (not the only generate month).
-- Apply: sqlcmd -S "(localdb)\MSSQLLocalDB" -d Account -i thisfile.sql
SET NOCOUNT ON;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Pay_BonusGenerate_Candidates
    @TenantId INT,
    @BenefitRuleId INT,
    @Year INT,
    @Month INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @PeriodStart date = DATEFROMPARTS(@Year, @Month, 1);
    DECLARE @PeriodEnd date = EOMONTH(@PeriodStart);

    DECLARE
        @RuleScale nvarchar(500),
        @RuleOrgId int,
        @RuleMinService decimal(9,2),
        @RuleMinSalary decimal(18,2),
        @RuleServiceStatus nvarchar(80),
        @RuleIsIneligible bit,
        @RuleValidFrom date,
        @RuleFound bit = 0;

    SELECT
        @RuleFound = 1,
        @RuleScale = NULLIF(LTRIM(RTRIM(r.Scale)), N''),
        @RuleOrgId = r.OrganizationId,
        @RuleMinService = COALESCE(r.MinimumService, 0),
        @RuleMinSalary = COALESCE(r.MinimumSalary, 0),
        @RuleServiceStatus = NULLIF(LTRIM(RTRIM(r.ServiceStatus)), N''),
        @RuleIsIneligible = r.IsIneligible,
        @RuleValidFrom = r.ValidFrom
    FROM dbo.PayrollBenefitRules r
    WHERE r.TenantId = @TenantId
      AND r.Id = @BenefitRuleId
      AND r.BenefitsType = N'Bonus';

    IF @RuleFound = 0
    BEGIN
        SELECT
            CAST(NULL AS uniqueidentifier) AS PersonId,
            CAST(NULL AS uniqueidentifier) AS StaffId,
            CAST(NULL AS nvarchar(50)) AS EmployeeNumber,
            CAST(NULL AS nvarchar(200)) AS FullName,
            CAST(NULL AS nvarchar(120)) AS Designation,
            CAST(NULL AS nvarchar(120)) AS Department,
            CAST(NULL AS date) AS DateOfJoining,
            CAST(NULL AS nvarchar(80)) AS Scale,
            CAST(0 AS int) AS ServiceMonths,
            CAST(0 AS decimal(9,2)) AS ServiceYears,
            CAST(0 AS decimal(18,2)) AS BaseSalary,
            CAST(NULL AS int) AS ParameterId,
            CAST(0 AS bit) AS IsValid,
            CAST(NULL AS nvarchar(500)) AS ValidationMessage,
            CAST(0 AS decimal(18,2)) AS BonusAmount,
            CAST(0 AS decimal(9,4)) AS BasicPercent,
            CAST(0 AS decimal(9,4)) AS ServicePercent,
            CAST(0 AS decimal(9,4)) AS AttendancePercent,
            CAST(0 AS decimal(9,4)) AS AssessmentPercent,
            CAST(0 AS decimal(9,4)) AS LeavePercent,
            CAST(0 AS decimal(9,4)) AS DisciplinePercent,
            CAST(1 AS int) AS Installment,
            CAST(0 AS int) AS CurrentInstallmentNo
        WHERE 1 = 0;
        RETURN;
    END;

    ;WITH OrgUp AS
    (
        SELECT o.Id, o.ParentId, o.Id AS StartId
        FROM dbo.OrganizationTree o
        WHERE @RuleOrgId IS NOT NULL
          AND o.Id IN (
              SELECT DISTINCT s.OrganizationId
              FROM dbo.vw_StaffDirectory s
              WHERE s.TenantId = @TenantId
                AND s.IsPersonActive = 1
                AND s.OrganizationId IS NOT NULL
          )
        UNION ALL
        SELECT p.Id, p.ParentId, c.StartId
        FROM dbo.OrganizationTree p
        INNER JOIN OrgUp c ON c.ParentId = p.Id
    ),
    StaffReady AS
    (
        SELECT
            s.PersonId,
            s.StaffId,
            s.EmployeeId AS EmployeeNumber,
            s.FullName,
            s.Designation,
            s.Department,
            CAST(hr.JoiningDate AS date) AS DateOfJoining,
            hr.Scale,
            CAST(CASE WHEN hr.PersonId IS NULL THEN 0 ELSE 1 END AS bit) AS HasHrProfile,
            COALESCE(NULLIF(hr.CurrentPay, 0), NULLIF(hr.BasicSalary, 0), 0) AS Salary,
            NULLIF(hr.BasicSalary, 0) AS BasicSalary,
            NULLIF(hr.CurrentPay, 0) AS CurrentPay,
            p.EmploymentStatus,
            CASE
                WHEN hr.JoiningDate IS NULL OR CAST(hr.JoiningDate AS date) > @PeriodEnd THEN 0
                ELSE CASE
                    WHEN DATEDIFF(MONTH, CAST(hr.JoiningDate AS date), @PeriodEnd)
                         - CASE WHEN DAY(@PeriodEnd) < DAY(CAST(hr.JoiningDate AS date)) THEN 1 ELSE 0 END < 0 THEN 0
                    ELSE DATEDIFF(MONTH, CAST(hr.JoiningDate AS date), @PeriodEnd)
                         - CASE WHEN DAY(@PeriodEnd) < DAY(CAST(hr.JoiningDate AS date)) THEN 1 ELSE 0 END
                END
            END AS ServiceMonths,
            CAST(CASE
                WHEN @RuleOrgId IS NULL THEN 1
                WHEN s.OrganizationId IS NULL THEN 0
                WHEN EXISTS (
                    SELECT 1 FROM OrgUp u
                    WHERE u.StartId = s.OrganizationId AND u.Id = @RuleOrgId
                ) THEN 1
                ELSE 0
            END AS bit) AS OrgMatch,
            CAST(CASE
                WHEN @RuleScale IS NULL THEN 1
                WHEN hr.Scale IS NULL OR LTRIM(RTRIM(hr.Scale)) = N'' THEN 0
                WHEN EXISTS (
                    SELECT 1
                    FROM STRING_SPLIT(@RuleScale, N',') ss
                    WHERE LOWER(LTRIM(RTRIM(ss.value))) = LOWER(LTRIM(RTRIM(hr.Scale)))
                      AND LTRIM(RTRIM(ss.value)) <> N''
                ) THEN 1
                ELSE 0
            END AS bit) AS ScaleMatch,
            CAST(CASE
                WHEN @RuleMinSalary <= 0 THEN 1
                WHEN COALESCE(NULLIF(hr.CurrentPay, 0), NULLIF(hr.BasicSalary, 0), 0) >= @RuleMinSalary THEN 1
                ELSE 0
            END AS bit) AS SalaryMatch
        FROM dbo.vw_StaffDirectory s
        LEFT JOIN dbo.PersonHrProfiles hr
            ON hr.PersonId = s.PersonId
           AND hr.TenantId = s.TenantId
        LEFT JOIN dbo.Persons p
            ON p.PersonId = s.PersonId
        WHERE s.TenantId = @TenantId
          AND s.IsPersonActive = 1
    ),
    DistReady AS
    (
        SELECT
            p.Id AS ParameterId,
            p.MinimumService,
            p.AmountType,
            p.PayType,
            p.Amount,
            p.Percentage,
            COALESCE(d.BasicPercentage, 0) AS BasicPercentage,
            COALESCE(d.ServicePercentage, 0) AS ServicePercentage,
            COALESCE(d.ServiceYears, 0) AS DistributionServiceYears,
            COALESCE(d.AssessmentPercentage, 0) AS AssessmentPercentage,
            COALESCE(d.AttendancePercentage, 0) AS AttendancePercentage,
            COALESCE(d.LeavePercentage, 0) AS LeavePercentage,
            COALESCE(d.DisciplinePercentage, 0) AS DisciplinePercentage,
            COALESCE(NULLIF(d.Installments, 0), 1) AS Installments,
            -- Installment schedule start: explicit date, else Month + rule ValidFrom year (or generate year).
            CAST(COALESCE(
                d.InstallmentStart,
                CASE
                    WHEN d.Month IS NULL THEN NULL
                    ELSE DATEFROMPARTS(
                        COALESCE(YEAR(@RuleValidFrom), @Year),
                        d.Month,
                        1)
                END
            ) AS date) AS EffectiveStart,
            d.InstallmentEnd,
            d.Month AS DistMonth
        FROM dbo.PayrollBenefitParameters p
        INNER JOIN dbo.PayrollBonusDistributions d
            ON d.BenefitParameterId = p.Id
           AND d.TenantId = p.TenantId
        WHERE p.BenefitRuleId = @BenefitRuleId
          AND p.TenantId = @TenantId
    ),
    DistWindow AS
    (
        SELECT
            dr.*,
            CAST(DATEFROMPARTS(YEAR(dr.EffectiveStart), MONTH(dr.EffectiveStart), 1) AS date) AS StartMonth,
            CAST(
                CASE
                    WHEN dr.InstallmentEnd IS NOT NULL THEN EOMONTH(dr.InstallmentEnd)
                    WHEN dr.EffectiveStart IS NOT NULL
                        THEN EOMONTH(DATEADD(MONTH, dr.Installments - 1, DATEFROMPARTS(YEAR(dr.EffectiveStart), MONTH(dr.EffectiveStart), 1)))
                    ELSE NULL
                END
            AS date) AS EndMonth
        FROM DistReady dr
    ),
    RankedParams AS
    (
        SELECT
            sc.PersonId,
            dw.ParameterId,
            dw.AmountType,
            dw.PayType,
            dw.Amount,
            dw.Percentage,
            dw.BasicPercentage,
            dw.ServicePercentage,
            dw.DistributionServiceYears,
            dw.AssessmentPercentage,
            dw.AttendancePercentage,
            dw.LeavePercentage,
            dw.DisciplinePercentage,
            dw.Installments,
            dw.EffectiveStart,
            dw.InstallmentEnd,
            dw.StartMonth,
            dw.EndMonth,
            CASE
                WHEN dw.StartMonth IS NULL THEN 1
                ELSE (YEAR(@PeriodStart) - YEAR(dw.StartMonth)) * 12
                     + (MONTH(@PeriodStart) - MONTH(dw.StartMonth))
                     + 1
            END AS CurrentInstallmentNo,
            ROW_NUMBER() OVER (
                PARTITION BY sc.PersonId
                ORDER BY pmin.MinimumService DESC, dw.ParameterId DESC
            ) AS rn
        FROM StaffReady sc
        INNER JOIN DistWindow dw ON 1 = 1
        INNER JOIN dbo.PayrollBenefitParameters pmin
            ON pmin.Id = dw.ParameterId
        WHERE sc.ServiceMonths >= dw.MinimumService
          AND (
                dw.StartMonth IS NULL
                OR (
                    @PeriodStart >= dw.StartMonth
                    AND (dw.EndMonth IS NULL OR @PeriodStart <= dw.EndMonth)
                )
              )
    ),
    BestParam AS
    (
        SELECT * FROM RankedParams WHERE rn = 1
    )
    SELECT
        sc.PersonId,
        sc.StaffId,
        sc.EmployeeNumber,
        sc.FullName,
        sc.Designation,
        sc.Department,
        sc.DateOfJoining,
        sc.Scale,
        sc.ServiceMonths,
        CAST(FLOOR(sc.ServiceMonths / 12.0) AS decimal(9,2)) AS ServiceYears,
        sc.Salary AS BaseSalary,
        bp.ParameterId,
        CAST(CASE WHEN msg.ValidationMessage = N'' THEN 1 ELSE 0 END AS bit) AS IsValid,
        CASE WHEN msg.ValidationMessage = N'' THEN N'Eligible' ELSE msg.ValidationMessage END AS ValidationMessage,
        CASE WHEN msg.ValidationMessage = N'' THEN amounts.BonusAmount ELSE CAST(0 AS decimal(18,2)) END AS BonusAmount,
        CASE WHEN msg.ValidationMessage = N'' THEN
            CASE
                WHEN bp.ParameterId IS NULL THEN CAST(100 AS decimal(9,4))
                -- Flat Figure / no distribution %: treat as 100% Basic so T-Bonus = BONUS AMT
                WHEN COALESCE(bp.BasicPercentage, 0)
                   + COALESCE(bp.ServicePercentage, 0)
                   + COALESCE(bp.AttendancePercentage, 0)
                   + COALESCE(bp.AssessmentPercentage, 0)
                   + COALESCE(bp.LeavePercentage, 0)
                   + COALESCE(bp.DisciplinePercentage, 0) <= 0
                    THEN CAST(100 AS decimal(9,4))
                ELSE CAST(bp.BasicPercentage AS decimal(9,4))
            END
        ELSE CAST(0 AS decimal(9,4)) END AS BasicPercent,
        CASE
            WHEN msg.ValidationMessage <> N'' OR bp.ParameterId IS NULL THEN CAST(0 AS decimal(9,4))
            WHEN bp.DistributionServiceYears > 0
                THEN CAST(bp.ServicePercentage * CASE
                        WHEN FLOOR(sc.ServiceMonths / 12.0) / bp.DistributionServiceYears > 1 THEN 1
                        ELSE FLOOR(sc.ServiceMonths / 12.0) / bp.DistributionServiceYears
                    END AS decimal(9,4))
            ELSE CAST(bp.ServicePercentage AS decimal(9,4))
        END AS ServicePercent,
        CASE WHEN msg.ValidationMessage = N'' THEN COALESCE(bp.AttendancePercentage, 0) ELSE CAST(0 AS decimal(9,4)) END AS AttendancePercent,
        CASE WHEN msg.ValidationMessage = N'' THEN COALESCE(bp.AssessmentPercentage, 0) ELSE CAST(0 AS decimal(9,4)) END AS AssessmentPercent,
        CASE WHEN msg.ValidationMessage = N'' THEN COALESCE(bp.LeavePercentage, 0) ELSE CAST(0 AS decimal(9,4)) END AS LeavePercent,
        CASE WHEN msg.ValidationMessage = N'' THEN COALESCE(bp.DisciplinePercentage, 0) ELSE CAST(0 AS decimal(9,4)) END AS DisciplinePercent,
        COALESCE(bp.Installments, 1) AS Installment,
        COALESCE(bp.CurrentInstallmentNo, 0) AS CurrentInstallmentNo
    FROM StaffReady sc
    LEFT JOIN BestParam bp ON bp.PersonId = sc.PersonId
    CROSS APPLY
    (
        SELECT CAST(ROUND(
            CASE
                WHEN bp.ParameterId IS NULL THEN 0
                WHEN LOWER(LTRIM(RTRIM(bp.AmountType))) = N'figure' THEN COALESCE(bp.Amount, 0)
                ELSE
                    (
                        CASE LOWER(LTRIM(RTRIM(bp.PayType)))
                            WHEN N'basic' THEN COALESCE(sc.BasicSalary, sc.Salary)
                            WHEN N'current' THEN COALESCE(sc.CurrentPay, sc.Salary)
                            WHEN N'currentpay' THEN COALESCE(sc.CurrentPay, sc.Salary)
                            ELSE sc.Salary
                        END
                    ) * COALESCE(bp.Percentage, 0) / 100.0
            END
        , 2) AS decimal(18,2)) AS BonusAmount
    ) amounts
    CROSS APPLY
    (
        SELECT COALESCE(STUFF(CONCAT(
            CASE WHEN sc.HasHrProfile = 0 THEN N'; HR profile is missing' ELSE N'' END,
            CASE
                WHEN sc.HasHrProfile = 1
                     AND LOWER(LTRIM(RTRIM(COALESCE(bp.AmountType, N'')))) <> N'figure'
                     AND sc.Salary <= 0
                    THEN N'; Salary is missing'
                ELSE N''
            END,
            CASE WHEN sc.ScaleMatch = 0 THEN N'; Scale not in rule selection' ELSE N'' END,
            CASE
                WHEN sc.SalaryMatch = 0
                    THEN N'; Salary below minimum '
                         + CONVERT(nvarchar(32), CAST(@RuleMinSalary AS decimal(18,2)))
                ELSE N''
            END,
            CASE WHEN sc.OrgMatch = 0 THEN N'; Organization / entitled scope does not match' ELSE N'' END,
            CASE
                WHEN sc.DateOfJoining IS NOT NULL AND sc.DateOfJoining > @PeriodEnd
                    THEN N'; Joined after this bonus period'
                ELSE N''
            END,
            CASE
                WHEN sc.ServiceMonths < @RuleMinService
                    THEN N'; Minimum service is '
                         + CONVERT(nvarchar(32), CAST(@RuleMinService AS decimal(18,2)))
                         + N' month(s) (DOJ service '
                         + CONVERT(nvarchar(32), sc.ServiceMonths)
                         + N' month(s))'
                ELSE N''
            END,
            CASE WHEN @RuleIsIneligible = 1 THEN N'; Rule is marked ineligible' ELSE N'' END,
            CASE
                WHEN @RuleServiceStatus IS NOT NULL
                     AND LOWER(@RuleServiceStatus) NOT IN (N'all', N'active')
                     AND (
                         sc.EmploymentStatus IS NULL
                         OR LOWER(LTRIM(RTRIM(sc.EmploymentStatus))) <> LOWER(@RuleServiceStatus)
                     )
                    THEN N'; Requires service status ' + @RuleServiceStatus
                ELSE N''
            END,
            CASE
                WHEN bp.ParameterId IS NULL
                    THEN N'; No matching bonus distribution for this month/service (check Inst_Start / Month / installments window)'
                ELSE N''
            END,
            CASE
                WHEN bp.ParameterId IS NOT NULL
                     AND bp.StartMonth IS NOT NULL
                     AND (bp.CurrentInstallmentNo < 1 OR bp.CurrentInstallmentNo > bp.Installments)
                    THEN N'; Outside installment count window'
                ELSE N''
            END,
            CASE
                WHEN bp.ParameterId IS NOT NULL AND amounts.BonusAmount <= 0
                    THEN N'; Bonus Figure / Pay Ref amount is not configured'
                ELSE N''
            END
        ), 1, 2, N''), N'') AS ValidationMessage
    ) msg
    ORDER BY sc.FullName
    OPTION (MAXRECURSION 100);
END
GO
