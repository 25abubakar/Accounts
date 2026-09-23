-- Wave 1: Pay + Attendance list stored procedures
-- Apply: sqlcmd -S "(localdb)\MSSQLLocalDB" -d Account -i thisfile.sql
SET NOCOUNT ON;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Pay_BenefitRules_List
    @TenantId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        r.Id,
        r.BenefitReference AS BenRef,
        r.BenefitsType,
        r.Name,
        r.Company,
        r.Entitled,
        r.Contract,
        r.Frequency,
        r.ValidFrom,
        r.ValidTo,
        r.MaximumExpense AS MaxExp,
        r.ServiceStatus AS SerStatus,
        r.Scale,
        r.Wef,
        r.MinimumService AS MinService,
        r.MinimumSalary AS MinSalary,
        r.MaximumPh AS MaxPh,
        r.MinimumPh AS MinPh,
        r.IsIneligible AS Ineligible,
        r.ShareType,
        r.CompanyShare AS CovShare,
        r.StaffShare,
        r.OrganizationId,
        r.CompanyName AS CompName
    FROM dbo.PayrollBenefitRules r
    WHERE r.TenantId = @TenantId
    ORDER BY r.Name;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_Pay_BenefitParameters_List
    @TenantId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        p.Id,
        r.Name AS RuleName,
        p.Name,
        p.Reference AS [Ref],
        COALESCE(r.Entitled, r.CompanyName) AS Entitled,
        p.BenefitRuleId AS BenefitId,
        r.Frequency AS FreqId,
        p.MinimumService AS MinSer,
        p.AmountType AS AmtType,
        p.PayType AS PayTypeId,
        p.Amount,
        p.Percentage,
        r.MaximumPh AS MaxPh,
        r.MinimumPh AS MinPh,
        p.CompanyShare AS CoyShare,
        p.StaffShare,
        r.BenefitsType,
        d.Month AS BonusMonth,
        d.InstallmentStart,
        d.InstallmentEnd,
        COALESCE(d.BasicPercentage, 0) AS BasicPercentage,
        COALESCE(d.ServicePercentage, 0) AS ServicePercentage,
        COALESCE(d.ServiceYears, 0) AS ServiceYears,
        COALESCE(d.AssessmentPercentage, 0) AS AssessmentPercentage,
        COALESCE(d.AttendancePercentage, 0) AS AttendancePercentage,
        COALESCE(d.LeavePercentage, 0) AS LeavePercentage,
        COALESCE(d.DisciplinePercentage, 0) AS DisciplinePercentage,
        COALESCE(d.Installments, 1) AS Installments
    FROM dbo.PayrollBenefitParameters p
    INNER JOIN dbo.PayrollBenefitRules r ON r.Id = p.BenefitRuleId AND r.TenantId = p.TenantId
    LEFT JOIN dbo.PayrollBonusDistributions d ON d.BenefitParameterId = p.Id AND d.TenantId = p.TenantId
    WHERE p.TenantId = @TenantId
    ORDER BY p.Name;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_Pay_BonusRules_List
    @TenantId INT
AS
BEGIN
    SET NOCOUNT ON;
    ;WITH Dist AS
    (
        SELECT
            p.BenefitRuleId,
            d.Month AS DistMonth,
            d.InstallmentStart,
            d.InstallmentEnd,
            d.Installments,
            ROW_NUMBER() OVER (
                PARTITION BY p.BenefitRuleId
                ORDER BY p.MinimumService DESC, p.Id DESC
            ) AS rn
        FROM dbo.PayrollBenefitParameters p
        INNER JOIN dbo.PayrollBonusDistributions d
            ON d.BenefitParameterId = p.Id
           AND d.TenantId = p.TenantId
        WHERE p.TenantId = @TenantId
    )
    SELECT
        r.Id,
        r.BenefitReference AS reference,
        r.Name,
        r.ValidFrom,
        r.ValidTo,
        r.Scale,
        r.Frequency,
        r.MaximumExpense AS maximumExpense,
        r.MinimumService,
        r.MinimumSalary,
        r.OrganizationId,
        r.Company,
        r.Entitled,
        -- Bonus period comes from Benefits Distribution (legacy: no UI month calendar).
        CAST(COALESCE(
            YEAR(d.InstallmentStart),
            YEAR(r.ValidFrom),
            YEAR(SYSUTCDATETIME())
        ) AS int) AS periodYear,
        CAST(COALESCE(
            MONTH(d.InstallmentStart),
            NULLIF(d.DistMonth, 0),
            MONTH(r.ValidFrom),
            1
        ) AS int) AS periodMonth,
        d.InstallmentStart AS installmentStart,
        d.InstallmentEnd AS installmentEnd,
        COALESCE(NULLIF(d.Installments, 0), 1) AS installments
    FROM dbo.PayrollBenefitRules r
    LEFT JOIN Dist d ON d.BenefitRuleId = r.Id AND d.rn = 1
    WHERE r.TenantId = @TenantId
      AND r.BenefitsType = N'Bonus'
      AND r.IsIneligible = 0
    ORDER BY r.ValidFrom DESC, r.Name;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_Pay_BonusLines_List
    @TenantId INT,
    @BenefitRuleId INT,
    @Year INT,
    @Month INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        l.Id, l.TenantId, l.BonusRunId, l.PersonId, l.StaffId, l.EmployeeNumber, l.FullName,
        l.Designation, l.Department, l.DateOfJoining, l.Scale, l.IsValid, l.ValidationMessage,
        l.BaseSalary, l.BonusAmount, l.BasicBonus, l.AttendanceBonus, l.LeaveBonus, l.DisciplineBonus,
        l.AssessmentBonus, l.ServiceBonus, l.ServiceYears, l.Month, l.Year, l.TotalBonus,
        l.BasicPercent, l.ServicePercent, l.AttendancePercent, l.AssessmentPercent, l.LeavePercent,
        l.DisciplinePercent, l.InstallmentAmount, l.Installment, l.CurrentInstallmentNo, l.PaidInstallmentCount,
        l.IsApproved, l.IsPaid, l.PaidOnUtc, l.IsInactive, l.Remarks, l.CreatedOnUtc, l.UpdatedOnUtc
    FROM dbo.PayrollBonusLines l
    INNER JOIN dbo.PayrollBonusRuns r ON r.Id = l.BonusRunId AND r.TenantId = l.TenantId
    WHERE l.TenantId = @TenantId
      AND r.BenefitRuleId = @BenefitRuleId
      AND r.Year = @Year
      AND r.Month = @Month
      AND l.IsInactive = 0
      -- Legacy StaffBonus: one-shot / fully paid installments leave the active chart.
      AND l.IsPaid = 0
    ORDER BY l.FullName;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_Pay_EobiSettings_List
    @TenantId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, TenantId, EmployeeRatePercentage, EmployerRatePercentage, MinimumWage,
           MaximumContributionBase, EffectiveFrom, EffectiveTo, IsActive, CreatedOnUtc, UpdatedOnUtc
    FROM dbo.EobiSettings
    WHERE TenantId = @TenantId
    ORDER BY EffectiveFrom DESC;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_Pay_EobiEligibility_List
    @TenantId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        COALESCE(e.Id, 0) AS id,
        d.PersonId AS personId,
        d.EmployeeId AS staffId,
        d.FullName AS fullName,
        e.EobiNumber AS eobiNo,
        e.EobiNumber AS eobiNumber,
        d.Department AS department,
        CASE WHEN hr.JoiningDate IS NULL THEN NULL ELSE CAST(hr.JoiningDate AS date) END AS doj,
        CAST(CASE WHEN e.Id IS NOT NULL AND e.IsEligible = 1 THEN 1 ELSE 0 END AS bit) AS isOn,
        CAST(CASE WHEN e.Id IS NOT NULL AND e.IsEligible = 1 THEN 1 ELSE 0 END AS bit) AS isEligible,
        COALESCE(e.EffectiveFrom, CAST(COALESCE(hr.JoiningDate, SYSUTCDATETIME()) AS date)) AS effectiveFrom,
        e.EffectiveTo AS effectiveTo,
        e.Remarks AS remarks
    FROM dbo.vw_StaffDirectory d
    LEFT JOIN dbo.EobiEligibilities e ON e.PersonId = d.PersonId AND e.TenantId = d.TenantId
    LEFT JOIN dbo.PersonHrProfiles hr ON hr.PersonId = d.PersonId
    WHERE d.TenantId = @TenantId
      AND d.IsPersonActive = 1
    ORDER BY d.FullName;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_Pay_StaffMonthlyEobi_List
    @TenantId INT,
    @Year INT,
    @Month INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        s.Id AS id,
        COALESCE(s.EobiRef, N'') AS eobiRef,
        s.StaffNumber AS staffId,
        s.FullName AS fullName,
        s.Department AS department,
        s.DateOfJoining AS doj,
        s.CompanyShare AS coyShare,
        s.StaffShare AS staffShare,
        s.TotalAmount AS totAmount,
        s.Remarks AS remarks,
        s.IsApproved AS isApproved,
        s.IsPaid AS isPaid
    FROM dbo.StaffMonthlyEobis s
    WHERE s.TenantId = @TenantId
      AND s.Year = @Year
      AND s.Month = @Month
    ORDER BY s.FullName;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_Pay_StaffTaxes_List
    @TenantId INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @MinMonthly DECIMAL(18,2) =
        (SELECT TOP 1 MinTaxAmt FROM dbo.PayrollTaxParameters
         WHERE TenantId = @TenantId AND IsActive = 1
         ORDER BY Id DESC);
    SET @MinMonthly = COALESCE(@MinMonthly, 0);

    SELECT
        t.Id AS id,
        t.TaxRef AS taxId,
        t.PersonId AS personId,
        t.StaffId AS staffGuid,
        t.StaffNumber AS staffId,
        t.FullName AS fullName,
        t.Department AS department,
        t.Designation AS designation,
        t.DateFrom AS dateFrom,
        t.DateTo AS dateTo,
        t.Frequency AS frequency,
        t.MonthlyPay AS min,
        t.MonthlyPay AS net,
        t.IncomePay AS maxSalary,
        t.IncomePay AS incomePay,
        t.TotMonth AS taxMonths,
        t.TaxAdjustment AS adjustment,
        t.TaxableIncome AS taxableIncome,
        t.TaxAmount AS taxAmount,
        t.MonthlyTaxAmt AS monthlyTaxAmt,
        t.PayMonth AS payMonth,
        t.NetTax AS netTax,
        t.MonthlyNetTax AS monthlyNetTax,
        t.ExtraAmount AS extraAmount,
        t.MonthlyPay AS monthlyPay,
        t.TotMonth AS totMonth,
        t.DedPercentage AS dedPercentage,
        t.IsActive AS isActive
    FROM dbo.PayrollStaffTaxes t
    WHERE t.TenantId = @TenantId
      AND t.MonthlyPay >= @MinMonthly
    ORDER BY t.FullName, t.DateFrom DESC;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_Pay_StaffTaxCandidates_List
    @TenantId INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @MinMonthly DECIMAL(18,2) =
        (SELECT TOP 1 MinTaxAmt FROM dbo.PayrollTaxParameters
         WHERE TenantId = @TenantId AND IsActive = 1
         ORDER BY Id DESC);
    SET @MinMonthly = COALESCE(@MinMonthly, 0);

    ;WITH ranked AS (
        SELECT
            d.PersonId,
            d.StaffId AS StaffGuid,
            d.EmployeeId AS StaffId,
            d.FullName,
            d.Department,
            d.Designation,
            CAST(COALESCE(
                NULLIF(hr.CurrentPay, 0),
                NULLIF(ss.CurrentPay, 0),
                NULLIF(hr.BasicSalary, 0),
                NULLIF(ss.BasicSalary, 0),
                0) AS decimal(18,2)) AS MonthlyPay,
            ROW_NUMBER() OVER (PARTITION BY d.PersonId ORDER BY d.FullName) AS rn
        FROM dbo.vw_StaffDirectory d
        LEFT JOIN dbo.PersonHrProfiles hr ON hr.PersonId = d.PersonId
        LEFT JOIN dbo.SalaryScales ss
            ON ss.TenantId = d.TenantId
           AND ss.IsActive = 1
           AND hr.Scale IS NOT NULL
           AND LTRIM(RTRIM(ss.ScaleName)) = LTRIM(RTRIM(hr.Scale))
        WHERE d.TenantId = @TenantId
          AND d.IsPersonActive = 1
    )
    SELECT PersonId, StaffGuid, StaffId, FullName, Department, Designation, MonthlyPay
    FROM ranked
    WHERE rn = 1
      AND MonthlyPay >= @MinMonthly
    ORDER BY FullName;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_Pay_TaxParameters_List
    @TenantId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, MinTaxAmt AS minTaxAmt, MinTaxAmt AS minMonthlyPay, DedPercentage AS dedPercentage, IsActive AS isActive
    FROM dbo.PayrollTaxParameters
    WHERE TenantId = @TenantId
    ORDER BY Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_Pay_TaxSlabs_List
    @TenantId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, TenantId, TaxYear, SlabName, FromAmount, ToAmount, RateType, FixedTaxAmount,
           RatePercentage, TotTax, IsActive, CreatedOnUtc, UpdatedOnUtc
    FROM dbo.PayrollTaxSlabs
    WHERE TenantId = @TenantId
    ORDER BY TaxYear DESC, FromAmount;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_Pay_SalaryScales_List
    @TenantId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        Id, ScaleName, DisplayOrder, RuleRegistrationId, ApplicableType,
        -- NULL ApplyAfter must not break non-nullable materializers; 0 = no delay.
        COALESCE(ApplyAfter, 0) AS ApplyAfter,
        IncrementMonth, IncrementMonths, ScaleType, PayMode, FrequencyType, ContractType, RateType,
        BasicSalary, MaximumSalary, YearlyIncrement, GrossSalary, CurrentPay,
        MedicalAllowance, TravellingAllowance, Other, IsActive
    FROM dbo.SalaryScales
    WHERE TenantId = @TenantId
    ORDER BY CASE WHEN DisplayOrder = 0 THEN 2147483647 ELSE DisplayOrder END, ScaleName;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_Pay_RuleRegistrations_List
    @TenantId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, RuleType, Name, DateFrom, DateTo
    FROM dbo.PayScaleRuleRegistrations
    WHERE TenantId = @TenantId
    ORDER BY DateFrom DESC, RuleType, Name;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_Pay_Allowances_List
    @TenantId INT,
    @AllowanceCategory NVARCHAR(20) = N'GENERAL'
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        a.Id,
        a.AllowanceReference AS AllowanceRef,
        a.Name AS AllowName,
        a.SalaryScaleId,
        s.ScaleName AS Scale,
        a.AllowanceTypeId,
        t.Name AS AllowanceType,
        a.ContractType,
        a.FrequencyType,
        a.RateType,
        a.PayType,
        a.PayValue,
        a.CalculatedValue,
        a.AllowanceCategory,
        a.DesignationId,
        j.TitleName AS DesignationName,
        a.ShiftLookupValueId,
        lv.ValueCode AS ShiftCode,
        lv.DisplayText AS ShiftName
    FROM dbo.PayScaleAllowances a
    LEFT JOIN dbo.SalaryScales s ON s.Id = a.SalaryScaleId
    LEFT JOIN PlatformTypes.AllowanceTypes t ON t.Id = a.AllowanceTypeId
    LEFT JOIN dbo.JobTitles j ON j.Id = a.DesignationId
    LEFT JOIN dbo.AppLookupValues lv ON lv.LookupValueId = a.ShiftLookupValueId
    WHERE a.TenantId = @TenantId
      AND a.AllowanceCategory = @AllowanceCategory
    ORDER BY a.Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_Pay_Tadas_List
    @TenantId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        t.Id,
        t.TadaReference AS TadaRef,
        t.Name,
        t.SalaryScaleId,
        s.ScaleName AS SalaryScaleName,
        t.TadaTypeId,
        tt.Name AS TadaType,
        t.ContractType,
        t.FrequencyType,
        t.RateType,
        t.PayValue,
        t.CalculatedValue
    FROM dbo.PayScaleTadas t
    INNER JOIN dbo.SalaryScales s ON s.Id = t.SalaryScaleId
    INNER JOIN PlatformTypes.TadaTypes tt ON tt.Id = t.TadaTypeId
    WHERE t.TenantId = @TenantId
    ORDER BY t.Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_Pay_Leaves_List
    @TenantId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        l.Id,
        l.LeaveReference AS LeaveRef,
        l.Name,
        l.SalaryScaleId,
        s.ScaleName AS SalaryScaleName,
        l.LeaveTypeId,
        lt.Name AS LeaveType,
        l.ContractType,
        l.FrequencyType,
        l.RateType,
        l.TotalLeave,
        l.ApplicableType,
        l.ApplicableAfter,
        l.ValueType,
        l.Type,
        l.ApplicableValue
    FROM dbo.PayScaleLeaves l
    INNER JOIN dbo.SalaryScales s ON s.Id = l.SalaryScaleId
    INNER JOIN PlatformTypes.LeaveTypes lt ON lt.Id = l.LeaveTypeId
    WHERE l.TenantId = @TenantId
    ORDER BY l.Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_Pay_Packages_List
    @TenantId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        p.Id, p.Code, p.Name, p.SalaryScaleId, s.ScaleName AS SalaryScaleName,
        p.PayRuleId, r.Name AS PayRuleName, p.IsActive, p.Description,
        p.AllowanceReference AS AllowanceRef,
        p.TadaReference AS TadaRef,
        p.LeaveReference AS LeaveRef
    FROM dbo.SalaryPackages p
    INNER JOIN dbo.SalaryScales s ON s.Id = p.SalaryScaleId
    INNER JOIN dbo.PayRules r ON r.Id = p.PayRuleId
    WHERE p.TenantId = @TenantId
    ORDER BY p.Name;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_Attendance_RuleSettings_List
    @TenantId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        Id, AttendanceEntryTypeId, AttendanceTypeCode, AttendanceTypeName, Reference, RuleName,
        WorkingMinutes, BeforeCheckInMinutes, AfterCheckOutMinutes, CheckInAdjustMinutes, CheckOutAdjustMinutes,
        AbsentAfterShiftStartMinutes, EarlyCheckoutAbsentAfterMinutes, MissingCheckoutAfterShiftEndMinutes,
        CameraVerificationToleranceMinutes, AccountLockAbsentDays, WeekendChargeValue, AdjustAbsentDays,
        ExtremeLateAfterMinutes, PlatformLateStatusId, PlatformExtremeLateStatusId,
        ExtremeEarlyDepartureAfterMinutes, PlatformEarlyDepartureStatusId, PlatformExtremeEarlyDepartureStatusId,
        IsApproved, IsActive, IsOvertimeBonusActive, IsCompletedLateDeductionActive,
        CompletedLateDeductionPercentage, Remarks
    FROM dbo.vw_AttendanceRuleSettings
    WHERE TenantId = @TenantId
    ORDER BY Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_Attendance_MapRules_List
    @TenantId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        Id, StaffId, AttendanceEntryTypeId, AttendanceTypeCode, AttendanceTypeName,
        ShiftCode, ShiftName, TimeFrom, TimeTo, IsOpenAttendance
    FROM dbo.vw_AttendanceMapRules
    WHERE TenantId = @TenantId
    ORDER BY Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_Attendance_LoginReport
    @TenantId INT,
    @DateFrom DATE,
    @DateTo DATE,
    @VisiblePersonIds NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Persons TABLE (PersonId UNIQUEIDENTIFIER PRIMARY KEY);
    INSERT INTO @Persons (PersonId)
    SELECT DISTINCT TRY_CONVERT(UNIQUEIDENTIFIER, value)
    FROM OPENJSON(@VisiblePersonIds)
    WHERE TRY_CONVERT(UNIQUEIDENTIFIER, value) IS NOT NULL;

    SELECT
        s.Id,
        s.StaffId,
        s.PersonId,
        COALESCE(st.LoginId, N'') AS EmployeeNumber,
        COALESCE(NULLIF(p.FullName, N''), s.IdentityUserId) AS EmployeeName,
        COALESCE(v.Department, N'') AS Department,
        COALESCE(j.TitleName, v.JobTitle, N'') AS Designation,
        s.SessionDate AS [Date],
        CONVERT(varchar(5), s.LoginUtc, 108) AS LoginTime,
        CASE WHEN s.LogoutUtc IS NULL THEN NULL ELSE CONVERT(varchar(5), s.LogoutUtc, 108) END AS LogoutTime,
        CASE
            WHEN s.LogoutUtc IS NOT NULL THEN CAST(FLOOR(DATEDIFF(SECOND, s.LoginUtc, s.LogoutUtc) / 60.0) AS INT)
            ELSE CAST(FLOOR(DATEDIFF(SECOND, s.LoginUtc, SYSUTCDATETIME()) / 60.0) AS INT)
        END AS WorkingMinutes,
        s.Source,
        s.IpAddress,
        s.Remarks
    FROM dbo.ApplicationLoginSessions s
    INNER JOIN @Persons vp ON vp.PersonId = s.PersonId
    LEFT JOIN dbo.Persons p ON p.PersonId = s.PersonId
    LEFT JOIN dbo.StaffVacancy st ON st.StaffId = s.StaffId
    LEFT JOIN dbo.Vacancies v ON v.VacancyId = st.VacancyId
    LEFT JOIN dbo.JobTitles j ON j.Id = v.JobTitleId
    LEFT JOIN dbo.AspNetUsers u ON u.Id = s.IdentityUserId
    WHERE s.TenantId = @TenantId
      AND s.SessionDate >= @DateFrom
      AND s.SessionDate <= @DateTo
      AND s.PersonId IS NOT NULL
      AND (u.Id IS NULL OR (ISNULL(u.IsTenantAdmin, 0) = 0 AND ISNULL(u.IsSuperAdmin, 0) = 0))
    ORDER BY s.LoginUtc DESC, EmployeeName;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_Attendance_TimingChart_Staff
    @TenantId INT,
    @VisiblePersonIds NVARCHAR(MAX),
    @CallerPersonId UNIQUEIDENTIFIER = NULL,
    @OrganizationWide BIT = 0
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Persons TABLE (PersonId UNIQUEIDENTIFIER PRIMARY KEY);
    INSERT INTO @Persons (PersonId)
    SELECT DISTINCT TRY_CONVERT(UNIQUEIDENTIFIER, value)
    FROM OPENJSON(@VisiblePersonIds)
    WHERE TRY_CONVERT(UNIQUEIDENTIFIER, value) IS NOT NULL;

    ;WITH walk AS (
        SELECT d.PersonId, d.OrganizationId AS NodeId, 0 AS Depth
        FROM dbo.vw_StaffDirectory d
        INNER JOIN @Persons vp ON vp.PersonId = d.PersonId
        WHERE d.TenantId = @TenantId AND d.IsPersonActive = 1
        UNION ALL
        SELECT w.PersonId, o.ParentId, w.Depth + 1
        FROM walk w
        INNER JOIN dbo.OrganizationTree o ON o.Id = w.NodeId
        WHERE w.Depth < 20 AND o.ParentId IS NOT NULL
    ),
    branch AS (
        SELECT w.PersonId, o.Name AS BranchName,
               ROW_NUMBER() OVER (PARTITION BY w.PersonId ORDER BY w.Depth) AS rn
        FROM walk w
        INNER JOIN dbo.OrganizationTree o ON o.Id = w.NodeId
        WHERE o.Label IN (N'Branch', N'Office')
    )
    SELECT
        d.PersonId,
        d.StaffId,
        d.EmployeeId,
        d.FullName,
        COALESCE(b.BranchName, org.Name, d.Department) AS BranchName,
        d.Department,
        d.Designation,
        d.PhotoUrl,
        CAST(CASE WHEN @CallerPersonId IS NOT NULL AND d.PersonId = @CallerPersonId THEN 1 ELSE 0 END AS bit) AS IsCurrentUser,
        CAST(CASE WHEN @OrganizationWide = 1 OR @CallerPersonId IS NULL OR d.PersonId <> @CallerPersonId THEN 1 ELSE 0 END AS bit) AS CanEditTiming
    FROM dbo.vw_StaffDirectory d
    INNER JOIN @Persons vp ON vp.PersonId = d.PersonId
    LEFT JOIN branch b ON b.PersonId = d.PersonId AND b.rn = 1
    LEFT JOIN dbo.OrganizationTree org ON org.Id = d.OrganizationId
    WHERE d.TenantId = @TenantId
      AND d.IsPersonActive = 1
    ORDER BY d.FullName
    OPTION (MAXRECURSION 100);
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_Attendance_TimingChart_StaffSchedule
    @TenantId INT,
    @Year INT,
    @Month INT,
    @VisiblePersonIds NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Persons TABLE (PersonId UNIQUEIDENTIFIER PRIMARY KEY);
    INSERT INTO @Persons (PersonId)
    SELECT DISTINCT TRY_CONVERT(UNIQUEIDENTIFIER, value)
    FROM OPENJSON(@VisiblePersonIds)
    WHERE TRY_CONVERT(UNIQUEIDENTIFIER, value) IS NOT NULL;

    SELECT
        d.PersonId,
        d.StaffId,
        d.EmployeeId,
        d.FullName,
        d.Department,
        d.Designation,
        d.PhotoUrl,
        d.ShiftStartTime,
        d.ShiftEndTime,
        sch.Id AS ScheduleId,
        sch.ScheduleDate,
        sch.HolidayTypeId,
        ht.ValueCode AS HolidayTypeCode,
        ht.DisplayText AS HolidayTypeName,
        sch.TimeFrom,
        sch.TimeTo,
        sch.WorkingMinutes,
        sch.IsOn,
        CAST(CASE WHEN sch.Id IS NULL THEN 0 ELSE 1 END AS bit) AS IsOverride
    FROM dbo.vw_StaffDirectory d
    INNER JOIN @Persons vp ON vp.PersonId = d.PersonId
    LEFT JOIN dbo.EmployeeTimingSchedules sch
        ON sch.StaffId = d.StaffId
       AND sch.TenantId = d.TenantId
       AND sch.ScheduleYear = @Year
       AND sch.ScheduleMonth = @Month
    LEFT JOIN dbo.AppLookupValues ht ON ht.LookupValueId = sch.HolidayTypeId
    WHERE d.TenantId = @TenantId
      AND d.IsPersonActive = 1
    ORDER BY d.FullName, sch.ScheduleDate;
END
GO

PRINT 'Wave1 Pay+Attendance list SPs created.';
