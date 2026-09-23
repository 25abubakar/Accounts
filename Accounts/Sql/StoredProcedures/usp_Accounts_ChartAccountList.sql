-- Chart of accounts grid list (main / sub / filtered). Tenant-scoped; keep in sync with migration 20260923150000_AddAccountsListProcedures.
CREATE OR ALTER PROCEDURE dbo.usp_Accounts_ChartAccountList
    @TenantId INT,
    @CategoryId INT = NULL,
    @ParentId INT = NULL,
    @ActiveOnly BIT = 0,
    @MainOnly BIT = 0
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        a.Id,
        a.CategoryId,
        cat.Name AS CategoryName,
        a.ParentId,
        a.AccountName,
        a.AccountNumber AS AccountNo,
        a.AccountCode,
        a.AccountReference,
        a.BankAccountNumber AS BankAccountNo,
        a.CnicNtn,
        a.Address,
        a.FullName,
        a.Email,
        a.Phone,
        a.DesignationId,
        a.PersonId,
        a.Description,
        a.Attachment,
        a.BudgetAmount,
        a.UsedAmount,
        a.BalanceAmount,
        a.AccountLimit,
        a.Credit,
        a.Debit,
        a.StatusId,
        a.IsStatement,
        a.IsInventory,
        a.IsStaff,
        a.IsActive
    FROM dbo.AccountsChartAccounts a
    LEFT JOIN dbo.AccountsCategories cat
        ON cat.Id = a.CategoryId
       AND cat.TenantId = a.TenantId
    WHERE a.TenantId = @TenantId
      AND (@CategoryId IS NULL OR a.CategoryId = @CategoryId)
      AND (@MainOnly = 0 OR a.ParentId IS NULL)
      AND (@ParentId IS NULL OR a.ParentId = @ParentId)
      AND (@ActiveOnly = 0 OR a.IsActive = 1)
    ORDER BY a.AccountName;
END
GO
