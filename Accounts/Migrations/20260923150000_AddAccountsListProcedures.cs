using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

/// <summary>
/// Category + chart-account list SPs for bulk grids.
/// Source: Sql/StoredProcedures/usp_Accounts_CategoryList.sql, usp_Accounts_ChartAccountList.sql
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260923150000_AddAccountsListProcedures")]
public sealed class AddAccountsListProcedures : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
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
            """);

        migrationBuilder.Sql(
            """
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
            """);

        // Support bulk list filters without scanning the whole table per request.
        migrationBuilder.Sql(
            """
            SET QUOTED_IDENTIFIER ON;
            SET ANSI_NULLS ON;

            IF NOT EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE name = N'IX_AccountsCategories_TenantId_Name'
                  AND object_id = OBJECT_ID(N'dbo.AccountsCategories'))
            BEGIN
                CREATE INDEX IX_AccountsCategories_TenantId_Name
                    ON dbo.AccountsCategories (TenantId, Name);
            END

            IF NOT EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE name = N'IX_AccountsChartAccounts_Tenant_Category_Parent_Active'
                  AND object_id = OBJECT_ID(N'dbo.AccountsChartAccounts'))
            BEGIN
                CREATE INDEX IX_AccountsChartAccounts_Tenant_Category_Parent_Active
                    ON dbo.AccountsChartAccounts (TenantId, CategoryId, ParentId, IsActive)
                    INCLUDE (AccountName, AccountNumber, AccountCode);
            END
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'dbo.usp_Accounts_ChartAccountList', N'P') IS NOT NULL
                DROP PROCEDURE dbo.usp_Accounts_ChartAccountList;
            IF OBJECT_ID(N'dbo.usp_Accounts_CategoryList', N'P') IS NOT NULL
                DROP PROCEDURE dbo.usp_Accounts_CategoryList;
            IF EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE name = N'IX_AccountsCategories_TenantId_Name'
                  AND object_id = OBJECT_ID(N'dbo.AccountsCategories'))
                DROP INDEX IX_AccountsCategories_TenantId_Name ON dbo.AccountsCategories;
            IF EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE name = N'IX_AccountsChartAccounts_Tenant_Category_Parent_Active'
                  AND object_id = OBJECT_ID(N'dbo.AccountsChartAccounts'))
                DROP INDEX IX_AccountsChartAccounts_Tenant_Category_Parent_Active ON dbo.AccountsChartAccounts;
            """);
    }
}
