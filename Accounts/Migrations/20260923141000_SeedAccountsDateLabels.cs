using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

/// <summary>
/// Seeds Account Type → Date tab labels (legacy tblAccountDates).
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260923141000_SeedAccountsDateLabels")]
public sealed class SeedAccountsDateLabels : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            INSERT INTO dbo.AccountsDateLabels (TenantId, Code, Name, IsActive)
            SELECT NULL, seed.Code, seed.Name, 1
            FROM (VALUES
                (N'ISSUE_DATE', N'Issue Date'),
                (N'TRANSACTION_DATE', N'Transaction Date'),
                (N'EFFECTIVE_DATE', N'Effective Date')
            ) seed(Code, Name)
            WHERE NOT EXISTS (
                SELECT 1 FROM dbo.AccountsDateLabels existing
                WHERE existing.Code = seed.Code AND existing.TenantId IS NULL
            );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DELETE FROM dbo.AccountsDateLabels
            WHERE TenantId IS NULL
              AND Code IN (N'ISSUE_DATE', N'TRANSACTION_DATE', N'EFFECTIVE_DATE');
            """);
    }
}
