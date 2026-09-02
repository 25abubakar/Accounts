SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @TenantId int = 2007;

BEGIN TRANSACTION;

WITH SourceRows AS
(
    SELECT *
    FROM (VALUES
        (N'RLT-3', N'Manager',       CAST(15000 AS decimal(18,4))),
        (N'RLT-4', N'Asst Manager',  CAST(10000 AS decimal(18,4))),
        (N'RLT-5', N'Supervisor',    CAST(5000 AS decimal(18,4))),
        (N'RLT-7', N'Depty Manager', CAST(10000 AS decimal(18,4)))
    ) source(ScaleName, DesignationName, PayValue)
), ResolvedRows AS
(
    SELECT
        scale.Id AS SalaryScaleId,
        allowanceType.Id AS AllowanceTypeId,
        designation.Id AS DesignationId,
        source.ScaleName,
        source.DesignationName,
        source.PayValue
    FROM SourceRows source
    INNER JOIN dbo.SalaryScales scale
        ON scale.TenantId = @TenantId
       AND scale.ScaleName = source.ScaleName
       AND scale.IsActive = 1
    INNER JOIN dbo.JobTitles designation
        ON designation.TenantId = @TenantId
       AND designation.TitleName = source.DesignationName
    INNER JOIN PlatformTypes.AllowanceTypes allowanceType
        ON allowanceType.TenantId = @TenantId
       AND allowanceType.Code = N'APPT'
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
    CONCAT(N'A-RLTA-', SUBSTRING(row.ScaleName, 5, 50)),
    row.DesignationName,
    row.SalaryScaleId,
    row.AllowanceTypeId,
    row.DesignationId,
    NULL,
    N'Regular',
    N'PM',
    N'Fixed',
    NULL,
    row.PayValue,
    ROUND(row.PayValue, 2),
    N'APPT',
    SYSUTCDATETIME(),
    NULL
FROM ResolvedRows row
WHERE NOT EXISTS
(
    SELECT 1
    FROM dbo.PayScaleAllowances existing
    WHERE existing.TenantId = @TenantId
      AND existing.AllowanceCategory = N'APPT'
      AND existing.SalaryScaleId = row.SalaryScaleId
      AND existing.AllowanceTypeId = row.AllowanceTypeId
      AND existing.DesignationId = row.DesignationId
);

DECLARE @InsertedRows int = @@ROWCOUNT;

COMMIT TRANSACTION;

SELECT @InsertedRows AS InsertedRows;
