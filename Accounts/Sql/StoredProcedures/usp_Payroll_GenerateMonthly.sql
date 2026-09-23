-- Phase 1: Payroll Create/Generate persistence + component resolution in SQL
-- (old project: SP_GetMonthlyStaffPayRoll → tblStaffPayRoll Id + PayRoll-{month}-{year} Ref).
-- Keep synchronized with Migrations/20260915180000_AddPayrollGenerateMonthlyProcedure.cs
-- Apply: sqlcmd -S "(localdb)\MSSQLLocalDB" -d Account -i thisfile.sql
--    or: dotnet ef database update (migration embeds CREATE OR ALTER)
SET NOCOUNT ON;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Payroll_GenerateMonthly
    @TenantId int,
    @Year int,
    @Month int,
    @PayDate date,
    @CreatedByUserId nvarchar(450),
    @CreatedByName nvarchar(150),
    @PayrollRunId bigint OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @Year < 2000 OR @Year > 2200 OR @Month < 1 OR @Month > 12
        THROW 51010, 'Enter a valid payroll month and year.', 1;

    DECLARE
        @PeriodStart date = DATEFROMPARTS(@Year, @Month, 1),
        @PeriodEnd date = EOMONTH(DATEFROMPARTS(@Year, @Month, 1)),
        @NowUtc datetime2 = SYSUTCDATETIME(),
        @RunNumber nvarchar(40) = N'PayRoll-' + CAST(@Month AS nvarchar(2)) + N'-' + CAST(@Year AS nvarchar(4)),
        @TaxYear nvarchar(20) = CASE
            WHEN @Month >= 7 THEN CONCAT(@Year, N'-', @Year + 1)
            ELSE CONCAT(@Year - 1, N'-', @Year)
        END,
        @ExistingStatus nvarchar(20),
        @PersonIdsJson nvarchar(max);

    SELECT
        @PayrollRunId = r.Id,
        @ExistingStatus = r.Status
    FROM dbo.PayrollRuns r
    WHERE r.TenantId = @TenantId
      AND r.Year = @Year
      AND r.Month = @Month;

    IF @PayrollRunId IS NOT NULL AND UPPER(COALESCE(@ExistingStatus, N'')) <> N'DRAFT'
        THROW 51011, 'Only a Draft payroll can be regenerated.', 1;

    BEGIN TRY
        BEGIN TRAN;

        IF @PayrollRunId IS NULL
        BEGIN
            INSERT INTO dbo.PayrollRuns
            (
                TenantId, Year, Month, RunNumber, PayDate, Status,
                CreatedByUserId, CreatedByName, CreatedOnUtc
            )
            VALUES
            (
                @TenantId, @Year, @Month, @RunNumber, @PayDate, N'Draft',
                @CreatedByUserId, @CreatedByName, @NowUtc
            );

            SET @PayrollRunId = CONVERT(bigint, SCOPE_IDENTITY());
        END
        ELSE
        BEGIN
            DELETE FROM dbo.PayrollLines
            WHERE TenantId = @TenantId
              AND PayrollRunId = @PayrollRunId;

            UPDATE dbo.PayrollRuns
            SET
                RunNumber = @RunNumber,
                PayDate = @PayDate,
                UpdatedOnUtc = @NowUtc,
                VerifiedByUserId = NULL,
                VerifiedByName = NULL,
                VerifiedOnUtc = NULL,
                ApprovedByUserId = NULL,
                ApprovedByName = NULL,
                ApprovedOnUtc = NULL,
                PaidByUserId = NULL,
                PaidByName = NULL,
                PaidOnUtc = NULL
            WHERE Id = @PayrollRunId
              AND TenantId = @TenantId;
        END;

        /* Active staff directory (same view as C# StaffDirectoryRows). */
        IF OBJECT_ID(N'tempdb..#staff') IS NOT NULL DROP TABLE #staff;
        SELECT
            d.PersonId,
            d.StaffId,
            CAST(COALESCE(d.EmployeeId, N'') AS nvarchar(50)) AS EmployeeNumber,
            CAST(COALESCE(d.FullName, N'') AS nvarchar(200)) AS FullName,
            CAST(d.Designation AS nvarchar(150)) AS Designation,
            CAST(d.Department AS nvarchar(150)) AS Department,
            d.OrganizationId,
            CAST(hr.JoiningDate AS date) AS DateOfJoining,
            CAST(hr.ScaleDate AS date) AS ScaleDate,
            CAST(NULLIF(LTRIM(RTRIM(hr.Scale)), N'') AS nvarchar(80)) AS HrScale,
            hr.SalaryPackageId,
            CAST(COALESCE(hr.CurrentPay, 0) AS decimal(18,2)) AS HrCurrentPay,
            CAST(COALESCE(hr.BasicSalary, 0) AS decimal(18,2)) AS HrBasicSalary,
            CAST(COALESCE(hr.IncrementSalary, 0) AS decimal(18,2)) AS HrIncrementSalary,
            CAST(COALESCE(hr.MaxSalary, 0) AS decimal(18,2)) AS HrMaxSalary,
            CAST(COALESCE(hr.SalaryAdjustment, 0) AS decimal(18,2)) AS SalaryAdjustment,
            CAST(NULLIF(LTRIM(RTRIM(hr.InductionType)), N'') AS nvarchar(50)) AS InductionType,
            CAST(v.JobTitleId AS int) AS DesignationId
        INTO #staff
        FROM dbo.vw_StaffDirectory d
        LEFT JOIN dbo.PersonHrProfiles hr
            ON hr.PersonId = d.PersonId
           AND hr.TenantId = d.TenantId
        LEFT JOIN dbo.StaffVacancy sv
            ON sv.StaffId = d.StaffId
        LEFT JOIN dbo.Vacancies v
            ON v.VacancyId = sv.VacancyId
        WHERE d.TenantId = @TenantId
          AND d.IsPersonActive = 1;

        /* Resolve package → scale, else HR Scale name. */
        IF OBJECT_ID(N'tempdb..#resolved') IS NOT NULL DROP TABLE #resolved;
        SELECT
            s.*,
            scale.Id AS SalaryScaleId,
            CAST(COALESCE(scale.ScaleName, s.HrScale) AS nvarchar(80)) AS EffectiveScale,
            CAST(COALESCE(NULLIF(s.HrCurrentPay, 0), NULLIF(s.HrBasicSalary, 0), NULLIF(scale.CurrentPay, 0), scale.BasicSalary, 0) AS decimal(18,2)) AS CurrentPay,
            CAST(COALESCE(NULLIF(s.HrBasicSalary, 0), scale.BasicSalary, 0) AS decimal(18,2)) AS ScaleBasicSalary,
            CAST(COALESCE(NULLIF(s.HrIncrementSalary, 0), scale.YearlyIncrement, 0) AS decimal(18,2)) AS IncrementSalary,
            CAST(COALESCE(NULLIF(s.HrMaxSalary, 0), scale.MaximumSalary, 0) AS decimal(18,2)) AS MaxSalary,
            CAST(COALESCE(scale.MedicalAllowance, 0) AS decimal(18,2)) AS ScaleMedical,
            CAST(COALESCE(scale.TravellingAllowance, 0) AS decimal(18,2)) AS ScaleTravel,
            CAST(COALESCE(scale.Other, 0) AS decimal(18,2)) AS ScaleOther,
            CAST(COALESCE(NULLIF(LTRIM(RTRIM(s.InductionType)), N''), NULLIF(LTRIM(RTRIM(scale.ContractType)), N'')) AS nvarchar(50)) AS ContractName,
            CAST(pkg.AllowanceReference AS nvarchar(100)) AS PackageAllowanceReference,
            CAST(pkg.TadaReference AS nvarchar(100)) AS PackageTadaReference,
            CAST(shiftMap.ShiftCode AS nvarchar(50)) AS ShiftCode,
            /* Completed months from DOJ through period end (day-adjusted). */
            CASE
                WHEN s.DateOfJoining IS NULL OR s.DateOfJoining > @PeriodEnd THEN 0
                ELSE
                    CASE
                        WHEN DAY(@PeriodEnd) < DAY(s.DateOfJoining)
                            THEN ((YEAR(@PeriodEnd) - YEAR(s.DateOfJoining)) * 12 + (MONTH(@PeriodEnd) - MONTH(s.DateOfJoining))) - 1
                        ELSE (YEAR(@PeriodEnd) - YEAR(s.DateOfJoining)) * 12 + (MONTH(@PeriodEnd) - MONTH(s.DateOfJoining))
                    END
            END AS ServiceMonths
        INTO #resolved
        FROM #staff s
        LEFT JOIN dbo.SalaryPackages pkg
            ON pkg.Id = s.SalaryPackageId
           AND pkg.TenantId = @TenantId
           AND pkg.IsActive = 1
        OUTER APPLY
        (
            SELECT TOP (1) sc.*
            FROM dbo.SalaryScales sc
            WHERE sc.TenantId = @TenantId
              AND sc.IsActive = 1
              AND (
                    (pkg.SalaryScaleId IS NOT NULL AND sc.Id = pkg.SalaryScaleId)
                 OR (pkg.SalaryScaleId IS NULL AND s.HrScale IS NOT NULL
                     AND UPPER(LTRIM(RTRIM(sc.ScaleName))) = UPPER(LTRIM(RTRIM(s.HrScale))))
              )
            ORDER BY CASE WHEN pkg.SalaryScaleId IS NOT NULL AND sc.Id = pkg.SalaryScaleId THEN 0 ELSE 1 END, sc.Id
        ) scale
        OUTER APPLY
        (
            SELECT TOP (1) am.ShiftCode
            FROM dbo.AttendanceMapRules am
            WHERE am.TenantId = @TenantId
              AND am.StaffId = s.StaffId
            ORDER BY COALESCE(am.ModifiedDate, am.CreatedDate) DESC
        ) shiftMap;

        UPDATE r
        SET r.ServiceMonths = CASE WHEN r.ServiceMonths < 0 THEN 0 ELSE r.ServiceMonths END
        FROM #resolved r;

        /* Latest AttendanceMap shift already on #resolved. Build VisiblePersonIds JSON. */
        SELECT @PersonIdsJson = CONCAT(
            N'[',
            STRING_AGG(CONCAT(N'"', CONVERT(nvarchar(36), PersonId), N'"'), N','),
            N']')
        FROM #resolved;

        IF @PersonIdsJson IS NULL
            SET @PersonIdsJson = N'[]';

        /* Attendance deduction report (same SP as Deduction screen). */
        IF OBJECT_ID(N'tempdb..#ded') IS NOT NULL DROP TABLE #ded;
        CREATE TABLE #ded
        (
            Id bigint NOT NULL,
            PersonId uniqueidentifier NOT NULL,
            StaffId uniqueidentifier NOT NULL,
            StaffNumber nvarchar(100) NULL,
            EmployeeName nvarchar(200) NULL,
            JobTitle nvarchar(200) NULL,
            Department nvarchar(200) NULL,
            [Month] int NOT NULL,
            [Year] int NOT NULL,
            PerDay decimal(18,2) NOT NULL,
            PerHour decimal(18,2) NOT NULL,
            MonthWorkingDays int NOT NULL,
            MonthWorkingMinutes int NOT NULL,
            MonthAttendanceMinutes int NOT NULL,
            FullMonthWorkingDays int NOT NULL,
            FullMonthWorkingMinutes int NOT NULL,
            AdjustAbsentDays int NOT NULL,
            OneDayWorkingMinutes int NOT NULL,
            NetShortMinutes int NOT NULL,
            LatePenaltyMinutes int NOT NULL,
            DeductibleMinutes int NOT NULL,
            NetOvertimeMinutes int NOT NULL,
            NetDeduction decimal(18,2) NOT NULL,
            OvertimeBonusAmount decimal(18,2) NOT NULL,
            IsOvertimeApproved bit NOT NULL,
            IsOvertimeBonusActive bit NOT NULL,
            AdjustmentAmount decimal(18,2) NOT NULL,
            IsAdjustmentApproved bit NOT NULL,
            AdjustmentRemarks nvarchar(255) NULL,
            FinalSalary decimal(18,2) NOT NULL,
            PendingReviewDays int NOT NULL,
            OpenDays int NOT NULL,
            LastFinalizedDate date NULL
        );

        IF EXISTS (SELECT 1 FROM #resolved)
        BEGIN
            INSERT INTO #ded
            EXEC dbo.usp_Attendance_DeductionReport
                @TenantId = @TenantId,
                @Year = @Year,
                @Month = @Month,
                @VisiblePersonIds = @PersonIdsJson;
        END;

        /* Allowances by category (exclude PROFICIENCY). */
        IF OBJECT_ID(N'tempdb..#allow') IS NOT NULL DROP TABLE #allow;
        SELECT
            r.PersonId,
            CAST(SUM(CASE WHEN UPPER(a.AllowanceCategory) = N'APPT' THEN a.CalculatedValue ELSE 0 END) AS decimal(18,2)) AS ApptAllowance,
            CAST(SUM(CASE
                WHEN (UPPER(a.AllowanceCategory) IN (N'SHIFT', N'NIGHT') OR UPPER(COALESCE(at.Code, N'')) = N'NIGHT')
                 AND UPPER(COALESCE(at.Code, N'')) = N'NIGHT'
                THEN a.CalculatedValue ELSE 0 END) AS decimal(18,2)) AS NightAllowance,
            CAST(SUM(CASE
                WHEN UPPER(a.AllowanceCategory) = N'SHIFT'
                 AND UPPER(COALESCE(at.Code, N'')) <> N'NIGHT'
                THEN a.CalculatedValue ELSE 0 END) AS decimal(18,2)) AS ShiftAllowance,
            CAST(SUM(CASE WHEN UPPER(COALESCE(at.Code, N'')) = N'MED' THEN a.CalculatedValue ELSE 0 END) AS decimal(18,2)) AS MedicalAllowance,
            CAST(SUM(CASE WHEN UPPER(COALESCE(at.Code, N'')) = N'TEL' THEN a.CalculatedValue ELSE 0 END) AS decimal(18,2)) AS TelephoneAllowance,
            CAST(SUM(CASE WHEN UPPER(COALESCE(at.Code, N'')) = N'TPT' THEN a.CalculatedValue ELSE 0 END) AS decimal(18,2)) AS TransportAllowance,
            CAST(SUM(CASE
                WHEN UPPER(a.AllowanceCategory) NOT IN (N'APPT', N'SHIFT', N'NIGHT')
                 AND UPPER(COALESCE(at.Code, N'')) NOT IN (N'MED', N'TEL', N'TPT', N'PROFICIENCY', N'NIGHT')
                THEN a.CalculatedValue ELSE 0 END) AS decimal(18,2)) AS GeneralAllowance,
            CAST(MAX(CASE WHEN UPPER(COALESCE(at.Code, N'')) = N'MED' THEN 1 ELSE 0 END) AS bit) AS HasMedicalConfig,
            CAST(MAX(CASE WHEN UPPER(COALESCE(at.Code, N'')) = N'TPT' THEN 1 ELSE 0 END) AS bit) AS HasTransportConfig,
            CAST(MAX(CASE
                WHEN r.SalaryScaleId IS NOT NULL
                 AND a.SalaryScaleId = r.SalaryScaleId
                 AND UPPER(a.AllowanceCategory) NOT IN (N'SHIFT', N'NIGHT')
                 AND UPPER(COALESCE(at.Code, N'')) <> N'PROFICIENCY'
                THEN 1 ELSE 0 END) AS bit) AS HasScaleAllowanceConfig
        INTO #allow
        FROM #resolved r
        INNER JOIN dbo.PayScaleAllowances a
            ON a.TenantId = @TenantId
           AND (
                (
                    UPPER(a.AllowanceCategory) IN (N'SHIFT', N'NIGHT')
                    AND (a.SalaryScaleId IS NULL OR a.SalaryScaleId = r.SalaryScaleId)
                )
                OR (r.SalaryScaleId IS NOT NULL AND a.SalaryScaleId = r.SalaryScaleId)
           )
           AND (
                (
                    UPPER(a.AllowanceCategory) = N'APPT'
                    AND a.DesignationId IS NOT NULL
                    AND a.DesignationId = r.DesignationId
                )
                OR (
                    UPPER(a.AllowanceCategory) IN (N'SHIFT', N'NIGHT')
                    AND a.ShiftLookupValueId IS NOT NULL
                    AND r.ShiftCode IS NOT NULL
                    AND EXISTS (
                        SELECT 1
                        FROM dbo.AppLookupValues lv
                        WHERE lv.LookupValueId = a.ShiftLookupValueId
                          AND UPPER(LTRIM(RTRIM(lv.ValueCode))) = UPPER(LTRIM(RTRIM(r.ShiftCode)))
                    )
                )
                OR UPPER(a.AllowanceCategory) NOT IN (N'APPT', N'SHIFT', N'NIGHT')
           )
           AND (
                r.PackageAllowanceReference IS NULL
                OR LTRIM(RTRIM(r.PackageAllowanceReference)) = N''
                OR UPPER(a.AllowanceCategory) IN (N'APPT', N'SHIFT', N'NIGHT')
                OR EXISTS (
                    SELECT 1
                    FROM STRING_SPLIT(REPLACE(r.PackageAllowanceReference, N';', N','), N',') refs
                    WHERE LTRIM(RTRIM(refs.value)) <> N''
                      AND UPPER(LTRIM(RTRIM(refs.value))) = UPPER(LTRIM(RTRIM(a.AllowanceReference)))
                )
           )
        INNER JOIN PlatformTypes.AllowanceTypes at
            ON at.Id = a.AllowanceTypeId
           AND UPPER(COALESCE(at.Code, N'')) <> N'PROFICIENCY'
        GROUP BY r.PersonId;

        /* TADA → general cash. */
        IF OBJECT_ID(N'tempdb..#tada') IS NOT NULL DROP TABLE #tada;
        SELECT
            r.PersonId,
            CAST(SUM(t.CalculatedValue) AS decimal(18,2)) AS TadaAmount
        INTO #tada
        FROM #resolved r
        INNER JOIN dbo.PayScaleTadas t
            ON t.TenantId = @TenantId
           AND r.SalaryScaleId IS NOT NULL
           AND t.SalaryScaleId = r.SalaryScaleId
           AND (
                r.PackageTadaReference IS NULL
                OR LTRIM(RTRIM(r.PackageTadaReference)) = N''
                OR EXISTS (
                    SELECT 1
                    FROM STRING_SPLIT(REPLACE(r.PackageTadaReference, N';', N','), N',') refs
                    WHERE LTRIM(RTRIM(refs.value)) <> N''
                      AND UPPER(LTRIM(RTRIM(refs.value))) = UPPER(LTRIM(RTRIM(t.TadaReference)))
                )
           )
        GROUP BY r.PersonId;

        /* Bonus: approved unpaid installment due for this payroll month. */
        IF OBJECT_ID(N'tempdb..#bonus') IS NOT NULL DROP TABLE #bonus;
        SELECT
            bl.PersonId,
            CAST(SUM(
                CASE
                    WHEN bl.InstallmentAmount > 0 THEN bl.InstallmentAmount
                    ELSE bl.TotalBonus
                END
            ) AS decimal(18,2)) AS BonusAmount
        INTO #bonus
        FROM dbo.PayrollBonusLines bl
        INNER JOIN dbo.PayrollBonusRuns br
            ON br.Id = bl.BonusRunId
           AND br.TenantId = bl.TenantId
        WHERE bl.TenantId = @TenantId
          AND bl.IsApproved = 1
          AND bl.IsInactive = 0
          AND bl.IsPaid = 0
          AND br.Status = N'Approved'
          AND (
                (@Year - bl.Year) * 12 + (@Month - bl.Month) >= 0
            AND (@Year - bl.Year) * 12 + (@Month - bl.Month) < CASE WHEN bl.Installment < 1 THEN 1 ELSE bl.Installment END
            AND (@Year - bl.Year) * 12 + (@Month - bl.Month) >= CASE WHEN bl.PaidInstallmentCount < 0 THEN 0 ELSE bl.PaidInstallmentCount END
          )
        GROUP BY bl.PersonId;

        /* Assessment: locked final approved posted amount for year/month. */
        IF OBJECT_ID(N'tempdb..#assess') IS NOT NULL DROP TABLE #assess;
        ;WITH Ranked AS
        (
            SELECT
                sa.SubjectPersonId AS PersonId,
                CAST(COALESCE(sa.Amount, 0) AS decimal(18,2)) AS AssessmentAmount,
                ROW_NUMBER() OVER (
                    PARTITION BY sa.SubjectPersonId
                    ORDER BY COALESCE(sa.SubmittedDateUtc, sa.ModifiedDateUtc, sa.CreatedDateUtc) DESC
                ) AS rn
            FROM dbo.StaffAssessments sa
            WHERE sa.TenantId = @TenantId
              AND sa.AssessmentYear = @Year
              AND sa.AssessmentMonth = @Month
              AND sa.IsLocked = 1
              AND sa.Amount IS NOT NULL
              AND sa.IsFinalApproved = 1
              AND sa.IsPostedToPayroll = 1
        )
        SELECT PersonId, AssessmentAmount
        INTO #assess
        FROM Ranked
        WHERE rn = 1;

        /* Simplified non-Bonus/non-EOBI benefits (scale + dates + min service; org descendant when scoped). */
        IF OBJECT_ID(N'tempdb..#benefits') IS NOT NULL DROP TABLE #benefits;
        ;WITH OrgAncestors AS
        (
            SELECT o.Id, o.ParentId, o.Id AS StartId
            FROM dbo.OrganizationTree o
            WHERE o.Id IN (SELECT DISTINCT OrganizationId FROM #resolved WHERE OrganizationId IS NOT NULL)
            UNION ALL
            SELECT p.Id, p.ParentId, c.StartId
            FROM dbo.OrganizationTree p
            INNER JOIN OrgAncestors c ON c.ParentId = p.Id
        ),
        Applicable AS
        (
            SELECT
                r.PersonId,
                benRule.Id AS RuleId,
                benRule.ShareType,
                benRule.CompanyShare,
                benRule.StaffShare,
                r.CurrentPay AS BasicSalary,
                r.ServiceMonths
            FROM #resolved r
            INNER JOIN dbo.PayrollBenefitRules benRule
                ON benRule.TenantId = @TenantId
               AND benRule.BenefitsType NOT IN (N'Bonus', N'EOBI')
               AND benRule.IsIneligible = 0
               AND (benRule.ValidFrom IS NULL OR benRule.ValidFrom <= @PeriodEnd)
               AND (benRule.ValidTo IS NULL OR benRule.ValidTo >= @PeriodStart)
               AND (benRule.Wef IS NULL OR benRule.Wef <= @PeriodEnd)
               AND r.ServiceMonths >= COALESCE(benRule.MinimumService, 0)
               AND (benRule.MinimumSalary <= 0 OR r.CurrentPay >= benRule.MinimumSalary)
               AND (
                    benRule.Scale IS NULL OR LTRIM(RTRIM(benRule.Scale)) = N''
                    OR EXISTS (
                        SELECT 1
                        FROM STRING_SPLIT(REPLACE(benRule.Scale, N';', N','), N',') sc
                        WHERE LTRIM(RTRIM(sc.value)) <> N''
                          AND r.EffectiveScale IS NOT NULL
                          AND UPPER(LTRIM(RTRIM(sc.value))) = UPPER(LTRIM(RTRIM(r.EffectiveScale)))
                    )
               )
               AND (
                    NOT EXISTS (SELECT 1 FROM dbo.PayrollBenefitRuleOrganizations o WHERE o.BenefitRuleId = benRule.Id AND o.TenantId = @TenantId)
                    AND (
                        benRule.OrganizationId IS NULL
                        OR EXISTS (
                            SELECT 1 FROM OrgAncestors oa
                            WHERE oa.StartId = r.OrganizationId AND oa.Id = benRule.OrganizationId
                        )
                    )
                    OR EXISTS (
                        SELECT 1
                        FROM dbo.PayrollBenefitRuleOrganizations scope
                        INNER JOIN OrgAncestors oa
                            ON oa.StartId = r.OrganizationId
                           AND oa.Id = scope.OrganizationId
                        WHERE scope.BenefitRuleId = benRule.Id
                          AND scope.TenantId = @TenantId
                    )
               )
        )
        SELECT
            a.PersonId,
            CAST(SUM(
                CASE
                    WHEN p.Id IS NOT NULL THEN
                        CASE
                            WHEN LOWER(COALESCE(p.AmountType, N'')) LIKE N'%percent%' OR CHARINDEX(N'%', COALESCE(p.AmountType, N'')) > 0
                                THEN ROUND(a.BasicSalary * p.CompanyShare / 100.0, 2)
                            ELSE ROUND(p.CompanyShare, 2)
                        END
                    WHEN LOWER(COALESCE(a.ShareType, N'')) LIKE N'%percent%' OR CHARINDEX(N'%', COALESCE(a.ShareType, N'')) > 0
                        THEN ROUND(a.BasicSalary * a.CompanyShare / 100.0, 2)
                    ELSE ROUND(a.CompanyShare, 2)
                END
            ) AS decimal(18,2)) AS EmployerBenefitAmount,
            CAST(SUM(
                CASE
                    WHEN p.Id IS NOT NULL THEN
                        CASE
                            WHEN LOWER(COALESCE(p.AmountType, N'')) LIKE N'%percent%' OR CHARINDEX(N'%', COALESCE(p.AmountType, N'')) > 0
                                THEN ROUND(a.BasicSalary * p.StaffShare / 100.0, 2)
                            ELSE ROUND(p.StaffShare, 2)
                        END
                    WHEN LOWER(COALESCE(a.ShareType, N'')) LIKE N'%percent%' OR CHARINDEX(N'%', COALESCE(a.ShareType, N'')) > 0
                        THEN ROUND(a.BasicSalary * a.StaffShare / 100.0, 2)
                    ELSE ROUND(a.StaffShare, 2)
                END
            ) AS decimal(18,2)) AS StaffBenefitDeduction
        INTO #benefits
        FROM Applicable a
        LEFT JOIN dbo.PayrollBenefitParameters p
            ON p.BenefitRuleId = a.RuleId
           AND p.TenantId = @TenantId
           AND a.ServiceMonths >= p.MinimumService
        GROUP BY a.PersonId
        OPTION (MAXRECURSION 100);

        /* EOBI eligibility + setting / EOBI benefit rule shares. */
        IF OBJECT_ID(N'tempdb..#eobi') IS NOT NULL DROP TABLE #eobi;
        ;WITH Eligible AS
        (
            SELECT DISTINCT e.PersonId
            FROM dbo.EobiEligibilities e
            WHERE e.TenantId = @TenantId
              AND e.IsEligible = 1
              AND e.EffectiveFrom <= @PeriodEnd
              AND (e.EffectiveTo IS NULL OR e.EffectiveTo >= @PeriodStart)
        ),
        Setting AS
        (
            SELECT TOP (1)
                s.EmployeeRatePercentage,
                s.EmployerRatePercentage,
                s.MinimumWage,
                s.MaximumContributionBase
            FROM dbo.EobiSettings s
            WHERE s.TenantId = @TenantId
              AND s.IsActive = 1
              AND s.EffectiveFrom <= @PeriodEnd
              AND (s.EffectiveTo IS NULL OR s.EffectiveTo >= @PeriodStart)
            ORDER BY s.EffectiveFrom DESC
        ),
        EobiRule AS
        (
            SELECT
                r.PersonId,
                CAST(COALESCE(param.StaffShare, benRule.StaffShare, 0) AS decimal(18,2)) AS EmployeeShare,
                CAST(COALESCE(param.CompanyShare, benRule.CompanyShare, 0) AS decimal(18,2)) AS EmployerShare,
                ROW_NUMBER() OVER (
                    PARTITION BY r.PersonId
                    ORDER BY
                        CASE WHEN benRule.Scale IS NOT NULL AND LTRIM(RTRIM(benRule.Scale)) <> N'' THEN 0 ELSE 1 END,
                        COALESCE(benRule.Wef, benRule.ValidFrom) DESC,
                        benRule.Id DESC
                ) AS rn
            FROM #resolved r
            INNER JOIN Eligible el ON el.PersonId = r.PersonId
            INNER JOIN dbo.PayrollBenefitRules benRule
                ON benRule.TenantId = @TenantId
               AND benRule.BenefitsType = N'EOBI'
               AND benRule.IsIneligible = 0
               AND (benRule.ValidFrom IS NULL OR benRule.ValidFrom <= @PeriodEnd)
               AND (benRule.ValidTo IS NULL OR benRule.ValidTo >= @PeriodStart)
               AND (benRule.Wef IS NULL OR benRule.Wef <= @PeriodEnd)
               AND (
                    benRule.Scale IS NULL OR LTRIM(RTRIM(benRule.Scale)) = N''
                    OR EXISTS (
                        SELECT 1
                        FROM STRING_SPLIT(REPLACE(benRule.Scale, N';', N','), N',') sc
                        WHERE LTRIM(RTRIM(sc.value)) <> N''
                          AND r.EffectiveScale IS NOT NULL
                          AND UPPER(LTRIM(RTRIM(sc.value))) = UPPER(LTRIM(RTRIM(r.EffectiveScale)))
                    )
               )
            OUTER APPLY
            (
                SELECT TOP (1) p.StaffShare, p.CompanyShare
                FROM dbo.PayrollBenefitParameters p
                WHERE p.BenefitRuleId = benRule.Id
                  AND p.TenantId = @TenantId
                ORDER BY p.Id DESC
            ) param
        )
        SELECT
            r.PersonId,
            CAST(CASE
                WHEN er.PersonId IS NOT NULL AND (er.EmployeeShare > 0 OR er.EmployerShare > 0)
                    THEN er.EmployeeShare
                WHEN el.PersonId IS NOT NULL AND st.EmployeeRatePercentage IS NOT NULL AND r.CurrentPay > 0 THEN
                    ROUND(
                        (CASE
                            WHEN st.MaximumContributionBase > 0
                                THEN CASE
                                    WHEN CASE WHEN r.CurrentPay > st.MinimumWage THEN r.CurrentPay ELSE st.MinimumWage END > st.MaximumContributionBase
                                        THEN st.MaximumContributionBase
                                    ELSE CASE WHEN r.CurrentPay > st.MinimumWage THEN r.CurrentPay ELSE st.MinimumWage END
                                END
                            ELSE CASE WHEN r.CurrentPay > st.MinimumWage THEN r.CurrentPay ELSE st.MinimumWage END
                        END) * st.EmployeeRatePercentage / 100.0
                    , 2)
                ELSE 0
            END AS decimal(18,2)) AS EmployeeEobiAmount,
            CAST(CASE
                WHEN er.PersonId IS NOT NULL AND (er.EmployeeShare > 0 OR er.EmployerShare > 0)
                    THEN er.EmployerShare
                WHEN el.PersonId IS NOT NULL AND st.EmployerRatePercentage IS NOT NULL AND r.CurrentPay > 0 THEN
                    ROUND(
                        (CASE
                            WHEN st.MaximumContributionBase > 0
                                THEN CASE
                                    WHEN CASE WHEN r.CurrentPay > st.MinimumWage THEN r.CurrentPay ELSE st.MinimumWage END > st.MaximumContributionBase
                                        THEN st.MaximumContributionBase
                                    ELSE CASE WHEN r.CurrentPay > st.MinimumWage THEN r.CurrentPay ELSE st.MinimumWage END
                                END
                            ELSE CASE WHEN r.CurrentPay > st.MinimumWage THEN r.CurrentPay ELSE st.MinimumWage END
                        END) * st.EmployerRatePercentage / 100.0
                    , 2)
                ELSE 0
            END AS decimal(18,2)) AS EmployerEobiAmount
        INTO #eobi
        FROM #resolved r
        LEFT JOIN Eligible el ON el.PersonId = r.PersonId
        LEFT JOIN EobiRule er ON er.PersonId = r.PersonId AND er.rn = 1
        OUTER APPLY (SELECT TOP (1) * FROM Setting) st;

        /* ContractId snapshot. */
        IF OBJECT_ID(N'tempdb..#contract') IS NOT NULL DROP TABLE #contract;
        SELECT
            r.PersonId,
            ct.Id AS ContractId
        INTO #contract
        FROM #resolved r
        OUTER APPLY
        (
            SELECT TOP (1) c.Id
            FROM PlatformTypes.ContractTypes c
            WHERE c.TenantId = @TenantId
              AND c.IsActive = 1
              AND r.ContractName IS NOT NULL
              AND (
                    UPPER(LTRIM(RTRIM(c.Name))) = UPPER(LTRIM(RTRIM(r.ContractName)))
                 OR UPPER(LTRIM(RTRIM(c.Code))) = UPPER(LTRIM(RTRIM(r.ContractName)))
              )
            ORDER BY c.Id
        ) ct;

        /* Assemble line components and insert. */
        IF OBJECT_ID(N'tempdb..#lines') IS NOT NULL DROP TABLE #lines;
        SELECT
            r.PersonId,
            r.StaffId,
            r.EmployeeNumber,
            r.FullName,
            r.Designation,
            r.Department,
            r.DateOfJoining,
            r.ScaleDate,
            r.EffectiveScale AS Scale,
            r.ContractName AS ContractType,
            c.ContractId,
            CAST(CASE WHEN r.CurrentPay > 0 THEN r.CurrentPay ELSE r.ScaleBasicSalary END AS decimal(18,2)) AS BasicSalary,
            r.ScaleBasicSalary,
            r.IncrementSalary,
            r.MaxSalary,
            r.CurrentPay,
            CAST(
                COALESCE(al.GeneralAllowance, 0)
              + COALESCE(td.TadaAmount, 0)
              + CASE WHEN COALESCE(al.HasScaleAllowanceConfig, 0) = 0 THEN r.ScaleOther ELSE 0 END
            AS decimal(18,2)) AS GeneralAllowanceAmount,
            CAST(COALESCE(al.ApptAllowance, 0) AS decimal(18,2)) AS ApptAllowanceAmount,
            CAST(COALESCE(al.ShiftAllowance, 0) AS decimal(18,2)) AS ShiftAllowanceAmount,
            CAST(CASE
                WHEN COALESCE(al.HasMedicalConfig, 0) = 1 THEN COALESCE(al.MedicalAllowance, 0)
                ELSE r.ScaleMedical
            END AS decimal(18,2)) AS MedicalAllowanceAmount,
            CAST(COALESCE(al.NightAllowance, 0) AS decimal(18,2)) AS NightAllowanceAmount,
            CAST(COALESCE(al.TelephoneAllowance, 0) AS decimal(18,2)) AS TelephoneAllowanceAmount,
            CAST(CASE
                WHEN COALESCE(al.HasTransportConfig, 0) = 1 THEN COALESCE(al.TransportAllowance, 0)
                ELSE r.ScaleTravel
            END AS decimal(18,2)) AS TransportAllowanceAmount,
            CAST(CASE WHEN r.SalaryAdjustment > 0 THEN r.SalaryAdjustment ELSE 0 END AS decimal(18,2)) AS SalaryAdjustment,
            CAST(COALESCE(bf.EmployerBenefitAmount, 0) AS decimal(18,2)) AS EmployerBenefitAmount,
            CAST(COALESCE(bf.StaffBenefitDeduction, 0) AS decimal(18,2)) AS StaffBenefitDeduction,
            CAST(COALESCE(asmt.AssessmentAmount, 0) AS decimal(18,2)) AS AssessmentAmount,
            CAST(COALESCE(bn.BonusAmount, 0) AS decimal(18,2)) AS BonusAmount,
            CAST(CASE
                WHEN COALESCE(d.IsOvertimeApproved, 0) = 1 AND COALESCE(d.IsOvertimeBonusActive, 0) = 1
                    THEN COALESCE(d.OvertimeBonusAmount, 0)
                ELSE 0
            END AS decimal(18,2)) AS OvertimeAmount,
            /* Gross attendance charge with AdjustAbsentDays hour-bank (parity with AttendanceService). */
            CAST(ROUND(
                (
                    CASE
                        WHEN COALESCE(d.DeductibleMinutes, 0) <= 0 THEN 0
                        ELSE
                            CASE
                                WHEN COALESCE(d.DeductibleMinutes, 0)
                                   - CASE
                                        WHEN COALESCE(d.AdjustAbsentDays, 0) * COALESCE(d.OneDayWorkingMinutes, 0) < COALESCE(d.DeductibleMinutes, 0)
                                            THEN COALESCE(d.AdjustAbsentDays, 0) * COALESCE(d.OneDayWorkingMinutes, 0)
                                        ELSE COALESCE(d.DeductibleMinutes, 0)
                                     END < 0
                                THEN 0
                                ELSE COALESCE(d.DeductibleMinutes, 0)
                                   - CASE
                                        WHEN COALESCE(d.AdjustAbsentDays, 0) * COALESCE(d.OneDayWorkingMinutes, 0) < COALESCE(d.DeductibleMinutes, 0)
                                            THEN COALESCE(d.AdjustAbsentDays, 0) * COALESCE(d.OneDayWorkingMinutes, 0)
                                        ELSE COALESCE(d.DeductibleMinutes, 0)
                                     END
                            END
                    END / 60.0
                ) * COALESCE(d.PerHour, 0)
            , 2) AS decimal(18,2)) AS AttendanceDeduction,
            CAST(COALESCE(d.AdjustmentAmount, 0) AS decimal(18,2)) AS AttendanceAdjustment,
            CAST(COALESCE(d.IsAdjustmentApproved, 0) AS bit) AS IsAttendanceAdjustmentApproved,
            CAST(d.AdjustmentRemarks AS nvarchar(255)) AS AttendanceAdjustmentRemarks,
            CAST(CASE WHEN COALESCE(d.PendingReviewDays, 0) > 0 THEN 1 ELSE 0 END AS bit) AS IsPending,
            CAST(COALESCE(d.PendingReviewDays, 0) AS int) AS PendingReviewDays,
            CAST(COALESCE(eo.EmployeeEobiAmount, 0) AS decimal(18,2)) AS EmployeeEobiAmount,
            CAST(COALESCE(eo.EmployerEobiAmount, 0) AS decimal(18,2)) AS EmployerEobiAmount
        INTO #lines
        FROM #resolved r
        LEFT JOIN #allow al ON al.PersonId = r.PersonId
        LEFT JOIN #tada td ON td.PersonId = r.PersonId
        LEFT JOIN #bonus bn ON bn.PersonId = r.PersonId
        LEFT JOIN #assess asmt ON asmt.PersonId = r.PersonId
        LEFT JOIN #benefits bf ON bf.PersonId = r.PersonId
        LEFT JOIN #eobi eo ON eo.PersonId = r.PersonId
        LEFT JOIN #ded d ON d.PersonId = r.PersonId
        LEFT JOIN #contract c ON c.PersonId = r.PersonId;

        /* Taxable monthly + slab tax (best-effort FBR-style). */
        IF OBJECT_ID(N'tempdb..#taxed') IS NOT NULL DROP TABLE #taxed;
        SELECT
            l.*,
            CAST(
                l.GeneralAllowanceAmount
              + l.ApptAllowanceAmount
              + l.ShiftAllowanceAmount
              + l.MedicalAllowanceAmount
              + l.NightAllowanceAmount
              + l.TelephoneAllowanceAmount
              + l.TransportAllowanceAmount
            AS decimal(18,2)) AS AllowanceAmount,
            CAST(
                l.BasicSalary
              + l.GeneralAllowanceAmount
              + l.ApptAllowanceAmount
              + l.ShiftAllowanceAmount
              + l.MedicalAllowanceAmount
              + l.NightAllowanceAmount
              + l.TelephoneAllowanceAmount
              + l.TransportAllowanceAmount
              + l.SalaryAdjustment
              + l.AssessmentAmount
              + l.BonusAmount
              + l.OvertimeAmount
            AS decimal(18,2)) AS TaxableMonthly
        INTO #taxed
        FROM #lines l;

        INSERT INTO dbo.PayrollLines
        (
            TenantId, PayrollRunId, PersonId, StaffId, EmployeeNumber, FullName,
            Designation, Department, DateOfJoining, ScaleDate, Scale, ContractType, ContractId,
            Month, Year,
            ScaleBasicSalary, IncrementSalary, MaxSalary, CurrentPay, BasicSalary,
            GeneralAllowanceAmount, ApptAllowanceAmount, ShiftAllowanceAmount,
            MedicalAllowanceAmount, NightAllowanceAmount, TelephoneAllowanceAmount, TransportAllowanceAmount,
            AllowanceAmount, SalaryAdjustment, AssessmentAmount,
            EmployerBenefitAmount, StaffBenefitDeduction,
            BonusAmount, OvertimeAmount,
            AttendanceDeduction, IsAttendanceDeductionActive,
            AttendanceAdjustment, IsAttendanceAdjustmentApproved, AttendanceAdjustmentRemarks,
            TaxableIncome, TaxAmount,
            EmployeeEobiAmount, EmployerEobiAmount, OtherDeduction,
            GrossPay, TotalDeduction, NetPay,
            IsPending, PendingReviewDays, IsApproved, IsPaid,
            Remarks, CreatedOnUtc
        )
        SELECT
            @TenantId,
            @PayrollRunId,
            t.PersonId,
            t.StaffId,
            t.EmployeeNumber,
            t.FullName,
            t.Designation,
            t.Department,
            t.DateOfJoining,
            t.ScaleDate,
            t.Scale,
            t.ContractType,
            t.ContractId,
            @Month,
            @Year,
            t.ScaleBasicSalary,
            t.IncrementSalary,
            t.MaxSalary,
            t.CurrentPay,
            t.BasicSalary,
            t.GeneralAllowanceAmount,
            t.ApptAllowanceAmount,
            t.ShiftAllowanceAmount,
            t.MedicalAllowanceAmount,
            t.NightAllowanceAmount,
            t.TelephoneAllowanceAmount,
            t.TransportAllowanceAmount,
            t.AllowanceAmount,
            t.SalaryAdjustment,
            t.AssessmentAmount,
            t.EmployerBenefitAmount,
            t.StaffBenefitDeduction,
            t.BonusAmount,
            t.OvertimeAmount,
            t.AttendanceDeduction,
            CAST(1 AS bit),
            t.AttendanceAdjustment,
            t.IsAttendanceAdjustmentApproved,
            t.AttendanceAdjustmentRemarks,
            t.TaxableMonthly, -- provisional; RecalculateRunTotals overwrites Gross/Taxable
            CAST(COALESCE((
                SELECT TOP (1)
                    CASE
                        WHEN t.TaxableMonthly <= 0 THEN 0
                        WHEN (t.TaxableMonthly * 12.0) <= 600000 THEN 0
                        WHEN slab.RatePercentage <= 0 AND slab.FixedTaxAmount <= 0 THEN 0
                        ELSE ROUND(
                            (
                                slab.FixedTaxAmount
                              + (
                                    CASE
                                        WHEN (t.TaxableMonthly * 12.0) - slab.FromAmount < 0 THEN 0
                                        ELSE (t.TaxableMonthly * 12.0) - slab.FromAmount
                                    END
                                ) * slab.RatePercentage / 100.0
                            ) / 12.0
                        , 2)
                    END
                FROM dbo.PayrollTaxSlabs slab
                WHERE slab.TenantId = @TenantId
                  AND slab.IsActive = 1
                  AND slab.TaxYear = @TaxYear
                  AND (t.TaxableMonthly * 12.0) > slab.FromAmount
                  AND (slab.ToAmount IS NULL OR (t.TaxableMonthly * 12.0) <= slab.ToAmount)
                ORDER BY slab.FromAmount
            ), 0) AS decimal(18,2)) AS TaxAmount,
            t.EmployeeEobiAmount,
            t.EmployerEobiAmount,
            CAST(0 AS decimal(18,2)) AS OtherDeduction,
            CAST(0 AS decimal(18,2)) AS GrossPay,
            CAST(0 AS decimal(18,2)) AS TotalDeduction,
            CAST(0 AS decimal(18,2)) AS NetPay,
            t.IsPending,
            t.PendingReviewDays,
            CAST(0 AS bit),
            CAST(0 AS bit),
            CASE
                WHEN t.BasicSalary <= 0 AND t.IsPending = 1
                    THEN N'Review: missing salary configuration (current/basic pay is zero). Pending Review attendance: '
                         + CAST(t.PendingReviewDays AS nvarchar(10)) + N' day(s) — blocks Process/Pay.'
                WHEN t.BasicSalary <= 0
                    THEN N'Review: missing salary configuration (current/basic pay is zero).'
                WHEN t.IsPending = 1
                    THEN N'Pending Review attendance: ' + CAST(t.PendingReviewDays AS nvarchar(10))
                         + N' day(s) — blocks Process/Pay.'
                ELSE NULL
            END,
            @NowUtc
        FROM #taxed t
        ORDER BY t.FullName, t.EmployeeNumber;

        EXEC dbo.usp_Payroll_RecalculateRunTotals
            @TenantId = @TenantId,
            @PayrollRunId = @PayrollRunId;

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRAN;
        THROW;
    END CATCH;
END;
GO
