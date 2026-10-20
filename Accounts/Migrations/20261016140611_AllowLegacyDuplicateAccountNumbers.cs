using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class AllowLegacyDuplicateAccountNumbers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE object_id = OBJECT_ID(N'dbo.AccountsChartAccounts')
                      AND name = N'IX_AccountsChartAccounts_Tenant_AccountNumber')
                    DROP INDEX IX_AccountsChartAccounts_Tenant_AccountNumber
                        ON dbo.AccountsChartAccounts;

                IF EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE object_id = OBJECT_ID(N'dbo.AccountsChartAccounts')
                      AND name = N'IX_AccountsChartAccounts_TenantId_AccountNumber')
                    DROP INDEX IX_AccountsChartAccounts_TenantId_AccountNumber
                        ON dbo.AccountsChartAccounts;

                CREATE INDEX IX_AccountsChartAccounts_TenantId_AccountNumber
                    ON dbo.AccountsChartAccounts (TenantId, AccountNumber);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE object_id = OBJECT_ID(N'dbo.AccountsChartAccounts')
                      AND name = N'IX_AccountsChartAccounts_TenantId_AccountNumber')
                    DROP INDEX IX_AccountsChartAccounts_TenantId_AccountNumber
                        ON dbo.AccountsChartAccounts;

                CREATE UNIQUE INDEX IX_AccountsChartAccounts_TenantId_AccountNumber
                    ON dbo.AccountsChartAccounts (TenantId, AccountNumber);
                """);
        }
    }
}
