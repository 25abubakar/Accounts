using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

/// <summary>Seeds the owner supplied Account &amp; Reconciliation chart accounts.</summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260928140000_SeedAccountReconciliationAccounts")]
public sealed class SeedAccountReconciliationAccounts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ;WITH RankedCategories AS
            (
                SELECT
                    c.TenantId,
                    c.Id AS CategoryId,
                    ROW_NUMBER() OVER (
                        PARTITION BY c.TenantId
                        ORDER BY CASE WHEN c.Code = N'ANR' THEN 0 ELSE 1 END, c.Id
                    ) AS RowNo
                FROM dbo.AccountsCategories c
                WHERE c.IsActive = 1
                  AND c.Name = N'Account & Reconciliation'
            ),
            ReconciliationCategories AS
            (
                SELECT TenantId, CategoryId
                FROM RankedCategories
                WHERE RowNo = 1
            ),
            SeedRows AS
            (
                SELECT
                    c.TenantId,
                    c.CategoryId,
                    v.AccountName,
                    v.AccountCode,
                    v.AccountNumber
                FROM ReconciliationCategories c
                CROSS JOIN (VALUES
                    (N'Missing Entries', N'ANR-1', N'ANR-1-0053'),
                    (N'Additional Entries', N'ANR-2', N'ANR-2-0054'),
                    (N'Deleted Entries', N'ANR-3', N'ANR-3-0055')
                ) v(AccountName, AccountCode, AccountNumber)
            )
            MERGE dbo.AccountsChartAccounts AS target
            USING SeedRows AS source
               ON target.TenantId = source.TenantId
              AND target.CategoryId = source.CategoryId
              AND (target.AccountNumber = source.AccountNumber OR target.AccountName = source.AccountName)
            WHEN MATCHED THEN
                UPDATE SET
                    target.ParentId = NULL,
                    target.AccountName = source.AccountName,
                    target.AccountCode = source.AccountCode,
                    target.AccountNumber = source.AccountNumber,
                    target.IsActive = 1,
                    target.UpdatedOnUtc = SYSUTCDATETIME()
            WHEN NOT MATCHED THEN
                INSERT
                (
                    TenantId, AccountNumber, AccountName, CategoryId, ParentId,
                    AccountCode, BudgetAmount, UsedAmount, BalanceAmount,
                    AccountLimit, Credit, Debit, StatusId, IsStatement,
                    IsInventory, IsStaff, IsActive, CreatedOnUtc
                )
                VALUES
                (
                    source.TenantId, source.AccountNumber, source.AccountName,
                    source.CategoryId, NULL, source.AccountCode,
                    0, 0, 0, 0, 0, 0, 11, 0, 0, 0, 1, SYSUTCDATETIME()
                );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DELETE account
            FROM dbo.AccountsChartAccounts account
            INNER JOIN dbo.AccountsCategories category
                ON category.Id = account.CategoryId
               AND category.TenantId = account.TenantId
            WHERE category.Name = N'Account & Reconciliation'
              AND account.AccountNumber IN (N'ANR-1-0053', N'ANR-2-0054', N'ANR-3-0055');
            """);
    }
}
