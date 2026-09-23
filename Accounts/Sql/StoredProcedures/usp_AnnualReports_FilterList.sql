CREATE OR ALTER PROCEDURE dbo.usp_AnnualReports_FilterList
    @TenantId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        f.Id,
        f.ReportTypeId,
        t.Name AS ReportType,
        f.CategoryId,
        c.Name AS Cat_Name,
        f.IsInclude
    FROM dbo.AnnualReportFilters f
    INNER JOIN dbo.AnnualReportTypes t ON t.Id = f.ReportTypeId
    INNER JOIN dbo.AccountsCategories c ON c.Id = f.CategoryId AND c.TenantId = f.TenantId
    WHERE f.TenantId = @TenantId
    ORDER BY t.Name, c.Name, f.Id;
END
GO
