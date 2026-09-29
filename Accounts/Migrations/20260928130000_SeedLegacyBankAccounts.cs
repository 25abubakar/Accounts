using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

/// <summary>Seeds the owner supplied legacy Bank chart accounts for every tenant that has a Bank category.</summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260928130000_SeedLegacyBankAccounts")]
public sealed class SeedLegacyBankAccounts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE name = N'IX_AccountsChartAccounts_TenantId_AccountCode'
                  AND object_id = OBJECT_ID(N'dbo.AccountsChartAccounts'))
            BEGIN
                DROP INDEX IX_AccountsChartAccounts_TenantId_AccountCode
                    ON dbo.AccountsChartAccounts;
            END;

            CREATE INDEX IX_AccountsChartAccounts_TenantId_AccountCode
                ON dbo.AccountsChartAccounts (TenantId, AccountCode)
                WHERE AccountCode IS NOT NULL;

            ;WITH RankedBankCategories AS
            (
                SELECT
                    c.TenantId,
                    c.Id AS CategoryId,
                    ROW_NUMBER() OVER (
                        PARTITION BY c.TenantId
                        ORDER BY CASE WHEN c.Code = N'BNK' THEN 0 ELSE 1 END, c.Id
                    ) AS RowNo
                FROM dbo.AccountsCategories c
                WHERE c.IsActive = 1
                  AND (c.Name = N'Bank' OR c.Code = N'BNK')
            ),
            BankCategories AS
            (
                SELECT TenantId, CategoryId
                FROM RankedBankCategories
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
                FROM BankCategories c
                CROSS JOIN (VALUES
                    (N'DIB-LT PKR', N'BNK-1', N'BNK-1-0001'),
                    (N'DIB-LT USD', N'BNK-2', N'BNK-2-0002'),
                    (N'DIB-LT GEN EXP', N'BNK-3', N'BNK-3-0003'),
                    (N'DIB-BB Cash Acct', N'BNK-4', N'BNK-4-0004'),
                    (N'ASK-LT PKR', N'BNK-5', N'BNK-5-0005'),
                    (N'ASK-LT USD', N'BNK-6', N'BNK-6-0006'),
                    (N'LT Receivable', N'BNK-7', N'BNK-7-0007'),
                    (N'LT Loan', N'BNK-8', N'BNK-8-0008'),
                    (N'Profit / Mark up', N'BNK-9', N'BNK-9-0095'),
                    (N'Bank Misc', N'BNK-10', N'BNK-10-0110'),
                    (N'Amazon Staff', N'BNK-10', N'BNK-10-0125'),
                    (N'Meezan Bank', N'BNK-10', N'BNK-10-0128'),
                    (N'0781740000029 ASK LT USD (Non Checking)', N'BNK-10', N'BNK-10-0162')
                ) v(AccountName, AccountCode, AccountNumber)
            )
            MERGE dbo.AccountsChartAccounts AS target
            USING SeedRows AS source
               ON target.TenantId = source.TenantId
              AND target.CategoryId = source.CategoryId
              AND (target.AccountNumber = source.AccountNumber OR target.AccountName = source.AccountName)
            WHEN MATCHED THEN
                UPDATE SET
                    target.CategoryId = source.CategoryId,
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
            DELETE FROM dbo.AccountsChartAccounts
            WHERE AccountNumber IN
            (
                N'BNK-1-0001', N'BNK-2-0002', N'BNK-3-0003', N'BNK-4-0004',
                N'BNK-5-0005', N'BNK-6-0006', N'BNK-7-0007', N'BNK-8-0008',
                N'BNK-9-0095', N'BNK-10-0110', N'BNK-10-0125',
                N'BNK-10-0128', N'BNK-10-0162'
            );

            DROP INDEX IX_AccountsChartAccounts_TenantId_AccountCode
                ON dbo.AccountsChartAccounts;
            CREATE UNIQUE INDEX IX_AccountsChartAccounts_TenantId_AccountCode
                ON dbo.AccountsChartAccounts (TenantId, AccountCode)
                WHERE AccountCode IS NOT NULL;
            """);
    }
}
