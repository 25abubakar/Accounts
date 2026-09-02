SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @TenantId int = 2007;

BEGIN TRANSACTION;

DECLARE @AllowanceTypeId int =
(
    SELECT TOP (1) allowanceType.Id
    FROM PlatformTypes.AllowanceTypes allowanceType
    WHERE allowanceType.TenantId = @TenantId
      AND allowanceType.Code = N'NIGHT'
      AND allowanceType.IsActive = 1
);

DECLARE @ShiftLookupValueId int =
(
    SELECT TOP (1) lookupValue.LookupValueId
    FROM dbo.AppLookupValues lookupValue
    INNER JOIN dbo.AppLookupTypes lookupType
        ON lookupType.LookupTypeId = lookupValue.LookupTypeId
    WHERE lookupValue.TenantId = @TenantId
      AND lookupType.LookupTypeCode = N'ATTENDANCE_SHIFT'
      AND lookupValue.ValueCode = N'NIGHT'
      AND lookupValue.IsActive = 1
      AND lookupType.IsActive = 1
);

IF @AllowanceTypeId IS NULL
BEGIN
    RAISERROR(N'Night allowance type was not found for tenant %d.', 16, 1, @TenantId);
END;

IF @ShiftLookupValueId IS NULL
BEGIN
    RAISERROR(N'Night shift lookup value was not found for tenant %d.', 16, 1, @TenantId);
END;

IF NOT EXISTS
(
    SELECT 1
    FROM dbo.PayScaleAllowances existing
    WHERE existing.TenantId = @TenantId
      AND existing.AllowanceCategory = N'SHIFT'
      AND existing.AllowanceReference = N'A-RLTN-'
      AND existing.AllowanceTypeId = @AllowanceTypeId
      AND existing.ShiftLookupValueId = @ShiftLookupValueId
)
BEGIN
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
    VALUES
    (
        @TenantId,
        N'A-RLTN-',
        N'Night',
        NULL,
        @AllowanceTypeId,
        NULL,
        @ShiftLookupValueId,
        N'Regular',
        N'PM',
        N'Fixed',
        NULL,
        CAST(5000 AS decimal(18,4)),
        CAST(5000 AS decimal(18,2)),
        N'SHIFT',
        SYSUTCDATETIME(),
        NULL
    );
END;

DECLARE @InsertedRows int = @@ROWCOUNT;

COMMIT TRANSACTION;

SELECT @InsertedRows AS InsertedRows;
