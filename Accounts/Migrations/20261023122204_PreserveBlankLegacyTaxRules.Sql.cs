namespace Accounts.Migrations;

public partial class PreserveBlankLegacyTaxRules
{
    private const string TaxSlabListSql = """
CREATE OR ALTER PROCEDURE dbo.usp_Pay_TaxSlabs_List
    @TenantId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, TenantId, TaxYear, SlabName, FromAmount, ToAmount, RateType, FixedTaxAmount,
           RatePercentage, TotTax, IsActive, CreatedOnUtc, UpdatedOnUtc
    FROM dbo.PayrollTaxSlabs
    WHERE TenantId = @TenantId
    ORDER BY Id;
END
""";
    private const string PreviousTaxSlabListSql = """
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
""";
    // Preserve SQL's existing tax-year selection. Only normalize NULL fixed-tax
    // cells to zero at the arithmetic boundary, not in the stored source data.
    private const string NullableFixedTaxSql = """
DECLARE @sql nvarchar(max)=OBJECT_DEFINITION(OBJECT_ID('dbo.usp_Payroll_GenerateMonthly'));
IF @sql IS NOT NULL AND @sql NOT LIKE '%COALESCE(slab.FixedTaxAmount%'
BEGIN
 SET @sql=REPLACE(@sql,'CREATE OR ALTER PROCEDURE','ALTER PROCEDURE');
 SET @sql=REPLACE(@sql,'CREATE   PROCEDURE','ALTER PROCEDURE');
 SET @sql=REPLACE(@sql,'CREATE PROCEDURE','ALTER PROCEDURE');
 SET @sql=REPLACE(@sql,'slab.FixedTaxAmount','COALESCE(slab.FixedTaxAmount, 0)');
 EXEC sys.sp_executesql @sql;
END;
""";
}
