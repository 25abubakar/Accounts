using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

/// <summary>
/// Adds the remaining category values used by the Reminder / Receivable form.
/// The seed is tenant-aware and can be executed safely when matching data
/// already exists.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260922090000_AddReminderReceivableCategories")]
public sealed class AddReminderReceivableCategories : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            SET XACT_ABORT ON;

            DECLARE @CategorySeed TABLE
            (
                Code nvarchar(40) NOT NULL,
                Name nvarchar(120) NOT NULL
            );

            INSERT @CategorySeed (Code, Name) VALUES
                (N'SAASC', N'SAASC'),
                (N'COMMON', N'Common'),
                (N'LT_STAFF', N'LT Staff'),
                (N'LAL_WONDERS_NSKZ', N'Lal Wonders (NSKZ)'),
                (N'MISC_EXP', N'Misc Exp'),
                (N'TAX', N'Tax'),
                (N'EOBI', N'EOBI'),
                (N'ENTERTAINMENT', N'Entertainment'),
                (N'RENT_RATE_TAXES', N'Rent Rate&Taxes'),
                (N'UTILITIES', N'Utilities'),
                (N'BANK_CHARGES', N'Bank Charges'),
                (N'INTERNET', N'Internet'),
                (N'ADMIN', N'Admin'),
                (N'ARSH_STUDIO', N'Arsh Studio'),
                (N'LAL_TECH', N'Lal Tech');

            UPDATE existing
            SET existing.Name = seed.Name,
                existing.IsActive = 1
            FROM dbo.AccountsCategories existing
            JOIN @CategorySeed seed ON seed.Code = existing.Code;

            UPDATE existing
            SET existing.IsActive = 1
            FROM dbo.AccountsCategories existing
            JOIN @CategorySeed seed ON seed.Name = existing.Name;

            INSERT dbo.AccountsCategories (TenantId, Code, Name, IsActive)
            SELECT tenant.Id, seed.Code, seed.Name, 1
            FROM dbo.Tenants tenant
            CROSS JOIN @CategorySeed seed
            WHERE NOT EXISTS
            (
                SELECT 1
                FROM dbo.AccountsCategories existing
                WHERE existing.TenantId = tenant.Id
                  AND (existing.Code = seed.Code OR existing.Name = seed.Name)
            );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Keep category master rows because they may already be referenced by
        // accounts, reminders, or Roznamcha entries.
    }
}
