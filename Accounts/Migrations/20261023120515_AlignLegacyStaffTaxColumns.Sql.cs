namespace Accounts.Migrations;

public partial class AlignLegacyStaffTaxColumns
{
    private const string StaffTaxListSql = """
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
        t.LegacyStaffTaxId AS staffTaxId,
        CAST(t.Id AS nvarchar(80)) AS taxId,
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
    ORDER BY t.LegacyStaffTaxId, t.FullName, t.DateFrom, t.Id;
END
""";
    private const string PreviousStaffTaxListSql = """
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
""";
}
