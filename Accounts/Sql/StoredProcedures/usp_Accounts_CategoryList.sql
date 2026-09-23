-- Account category grid list (Create Category / Accounts List). Tenant-scoped; keep in sync with migration 20260923150000_AddAccountsListProcedures.
CREATE OR ALTER PROCEDURE dbo.usp_Accounts_CategoryList
    @TenantId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        c.Id,
        c.CategoryTypeId,
        t.Name AS CategoryType,
        c.ReferenceNumber AS ReferenceNo,
        c.Name,
        c.Number,
        c.Code,
        c.BudgetAmount,
        c.UsedAmount,
        c.BalanceAmount,
        c.IsNotInReport,
        c.IsActive
    FROM dbo.AccountsCategories c
    LEFT JOIN dbo.AccountsCategoryTypes t
        ON t.Id = c.CategoryTypeId
       AND t.TenantId = c.TenantId
    WHERE c.TenantId = @TenantId
    ORDER BY c.Name;
END
GO
