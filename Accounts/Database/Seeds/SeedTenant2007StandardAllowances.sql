SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @TenantId int = 2007;

BEGIN TRANSACTION;

WITH SourceRows AS
(
    SELECT *
    FROM (VALUES
        (N'RLT-1',  N'TPT',         CAST(15 AS decimal(18,4)),    N'Percentage', N'Basic'),
        (N'RLT-1',  N'TEL',         CAST(5 AS decimal(18,4)),     N'Percentage', N'Basic'),
        (N'RLT-1',  N'MED',         CAST(10 AS decimal(18,4)),    N'Percentage', N'Basic'),
        (N'RLT-1',  N'PROFICIENCY', CAST(10000 AS decimal(18,4)), N'Fixed',      NULL),
        (N'RLT-2',  N'TPT',         CAST(15 AS decimal(18,4)),    N'Percentage', N'Basic'),
        (N'RLT-2',  N'TEL',         CAST(5 AS decimal(18,4)),     N'Percentage', N'Basic'),
        (N'RLT-2',  N'MED',         CAST(10 AS decimal(18,4)),    N'Percentage', N'Basic'),
        (N'RLT-2',  N'PROFICIENCY', CAST(10000 AS decimal(18,4)), N'Fixed',      NULL),
        (N'RLT-3',  N'TPT',         CAST(15 AS decimal(18,4)),    N'Percentage', N'Basic'),
        (N'RLT-3',  N'TEL',         CAST(5 AS decimal(18,4)),     N'Percentage', N'Basic'),
        (N'RLT-3',  N'MED',         CAST(10 AS decimal(18,4)),    N'Percentage', N'Basic'),
        (N'RLT-3',  N'PROFICIENCY', CAST(10000 AS decimal(18,4)), N'Fixed',      NULL),
        (N'RLT-4',  N'TPT',         CAST(15 AS decimal(18,4)),    N'Percentage', N'Basic'),
        (N'RLT-4',  N'TEL',         CAST(5 AS decimal(18,4)),     N'Percentage', N'Basic'),
        (N'RLT-4',  N'MED',         CAST(10 AS decimal(18,4)),    N'Percentage', N'Basic'),
        (N'RLT-4',  N'PROFICIENCY', CAST(10000 AS decimal(18,4)), N'Fixed',      NULL),
        (N'RLT-5',  N'TPT',         CAST(15 AS decimal(18,4)),    N'Percentage', N'Basic'),
        (N'RLT-5',  N'TEL',         CAST(5 AS decimal(18,4)),     N'Percentage', N'Basic'),
        (N'RLT-5',  N'MED',         CAST(10 AS decimal(18,4)),    N'Percentage', N'Basic'),
        (N'RLT-5',  N'PROFICIENCY', CAST(10000 AS decimal(18,4)), N'Fixed',      N'Basic'),
        (N'RLT-6',  N'TPT',         CAST(15 AS decimal(18,4)),    N'Percentage', N'Basic'),
        (N'RLT-6',  N'TEL',         CAST(5 AS decimal(18,4)),     N'Percentage', N'Basic'),
        (N'RLT-6',  N'MED',         CAST(10 AS decimal(18,4)),    N'Percentage', N'Basic'),
        (N'RLT-6',  N'PROFICIENCY', CAST(10000 AS decimal(18,4)), N'Fixed',      NULL),
        (N'RLT-7',  N'TPT',         CAST(15 AS decimal(18,4)),    N'Percentage', N'Basic'),
        (N'RLT-7',  N'TEL',         CAST(5 AS decimal(18,4)),     N'Percentage', N'Basic'),
        (N'RLT-7',  N'MED',         CAST(10 AS decimal(18,4)),    N'Percentage', N'Basic'),
        (N'RLT-7',  N'PROFICIENCY', CAST(10000 AS decimal(18,4)), N'Fixed',      N'Basic'),
        (N'RLT-7',  N'APPT',        CAST(10000 AS decimal(18,4)), N'Fixed',      NULL),
        (N'RLT-8',  N'TPT',         CAST(15 AS decimal(18,4)),    N'Percentage', N'Basic'),
        (N'RLT-8',  N'TEL',         CAST(5 AS decimal(18,4)),     N'Percentage', N'Basic'),
        (N'RLT-8',  N'MED',         CAST(10 AS decimal(18,4)),    N'Percentage', N'Basic'),
        (N'RLT-8',  N'PROFICIENCY', CAST(10000 AS decimal(18,4)), N'Fixed',      NULL),
        (N'RLT-9',  N'TPT',         CAST(15 AS decimal(18,4)),    N'Percentage', N'Basic'),
        (N'RLT-9',  N'TEL',         CAST(5 AS decimal(18,4)),     N'Percentage', N'Basic'),
        (N'RLT-9',  N'MED',         CAST(10 AS decimal(18,4)),    N'Percentage', N'Basic'),
        (N'RLT-9',  N'PROFICIENCY', CAST(10000 AS decimal(18,4)), N'Fixed',      NULL),
        (N'RLT-10', N'TPT',         CAST(15 AS decimal(18,4)),    N'Percentage', N'Basic'),
        (N'RLT-10', N'TEL',         CAST(5 AS decimal(18,4)),     N'Percentage', N'Basic'),
        (N'RLT-10', N'MED',         CAST(10 AS decimal(18,4)),    N'Percentage', N'Basic'),
        (N'RLT-10', N'PROFICIENCY', CAST(10000 AS decimal(18,4)), N'Fixed',      NULL),
        (N'RLT-11', N'TPT',         CAST(15 AS decimal(18,4)),    N'Percentage', N'Basic'),
        (N'RLT-11', N'TEL',         CAST(5 AS decimal(18,4)),     N'Percentage', N'Basic'),
        (N'RLT-11', N'MED',         CAST(10 AS decimal(18,4)),    N'Percentage', N'Basic'),
        (N'RLT-11', N'PROFICIENCY', CAST(10000 AS decimal(18,4)), N'Percentage', N'Basic'),
        (N'RLT-12', N'TPT',         CAST(15 AS decimal(18,4)),    N'Percentage', N'Basic'),
        (N'RLT-12', N'TEL',         CAST(5 AS decimal(18,4)),     N'Percentage', N'Basic'),
        (N'RLT-12', N'MED',         CAST(10 AS decimal(18,4)),    N'Percentage', N'Basic'),
        (N'RLT-12', N'PROFICIENCY', CAST(10000 AS decimal(18,4)), N'Percentage', NULL),
        (N'RLT-13', N'TPT',         CAST(15 AS decimal(18,4)),    N'Percentage', N'Basic'),
        (N'RLT-13', N'TEL',         CAST(5 AS decimal(18,4)),     N'Percentage', N'Basic'),
        (N'RLT-13', N'MED',         CAST(10 AS decimal(18,4)),    N'Percentage', N'Basic'),
        (N'RLT-13', N'PROFICIENCY', CAST(10000 AS decimal(18,4)), N'Percentage', NULL),
        (N'RLT-14', N'TPT',         CAST(15 AS decimal(18,4)),    N'Percentage', N'Basic'),
        (N'RLT-14', N'TEL',         CAST(5 AS decimal(18,4)),     N'Percentage', N'Basic'),
        (N'RLT-14', N'MED',         CAST(10 AS decimal(18,4)),    N'Percentage', N'Basic'),
        (N'RLT-14', N'PROFICIENCY', CAST(10000 AS decimal(18,4)), N'Fixed',      NULL),
        (N'RLT-15', N'TPT',         CAST(15 AS decimal(18,4)),    N'Percentage', N'Basic'),
        (N'RLT-15', N'TEL',         CAST(5 AS decimal(18,4)),     N'Percentage', N'Basic'),
        (N'RLT-15', N'MED',         CAST(10 AS decimal(18,4)),    N'Percentage', N'Basic'),
        (N'RLT-15', N'PROFICIENCY', CAST(10000 AS decimal(18,4)), N'Fixed',      NULL)
    ) source(ScaleName, AllowanceTypeCode, PayValue, RateType, PayType)
), ResolvedRows AS
(
    SELECT
        scale.Id AS SalaryScaleId,
        allowanceType.Id AS AllowanceTypeId,
        source.ScaleName,
        source.PayValue,
        source.RateType,
        source.PayType,
        CASE
            WHEN source.RateType = N'Percentage' AND source.PayType = N'Basic'
                THEN ROUND(scale.BasicSalary * source.PayValue / 100.0, 2)
            WHEN source.RateType = N'Fixed'
                THEN ROUND(source.PayValue, 2)
            ELSE CAST(0 AS decimal(18,2))
        END AS CalculatedValue
    FROM SourceRows source
    INNER JOIN dbo.SalaryScales scale
        ON scale.TenantId = @TenantId
       AND scale.ScaleName = source.ScaleName
       AND scale.IsActive = 1
    INNER JOIN PlatformTypes.AllowanceTypes allowanceType
        ON allowanceType.TenantId = @TenantId
       AND allowanceType.Code = source.AllowanceTypeCode
       AND allowanceType.IsActive = 1
)
INSERT INTO dbo.PayScaleAllowances
(
    TenantId,
    AllowanceReference,
    Name,
    SalaryScaleId,
    AllowanceTypeId,
    DesignationId,
    ShiftLookupValueId,
    ContractType,
    FrequencyType,
    RateType,
    PayType,
    PayValue,
    CalculatedValue,
    AllowanceCategory,
    CreatedOnUtc,
    UpdatedOnUtc
)
SELECT
    @TenantId,
    CONCAT(N'A-', row.ScaleName),
    N'Allowances_24',
    row.SalaryScaleId,
    row.AllowanceTypeId,
    NULL,
    NULL,
    N'Regular',
    N'PM',
    row.RateType,
    row.PayType,
    row.PayValue,
    row.CalculatedValue,
    N'GENERAL',
    SYSUTCDATETIME(),
    NULL
FROM ResolvedRows row
WHERE NOT EXISTS
(
    SELECT 1
    FROM dbo.PayScaleAllowances existing
    WHERE existing.TenantId = @TenantId
      AND existing.SalaryScaleId = row.SalaryScaleId
      AND existing.AllowanceTypeId = row.AllowanceTypeId
      AND existing.Name = N'Allowances_24'
      AND existing.AllowanceCategory = N'GENERAL'
);

DECLARE @InsertedRows int = @@ROWCOUNT;

COMMIT TRANSACTION;

SELECT @InsertedRows AS InsertedRows;
