using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260923100000_SeedPaymentRozLookups")]
public sealed class SeedPaymentRozLookups : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            INSERT INTO dbo.AccountsTransTypes (TenantId, Code, Name, IsActive)
            SELECT NULL, seed.Code, seed.Name, 1
            FROM (VALUES
                (N'PAYMENT', N'Payment'),
                (N'ADVANCE', N'Advance'),
                (N'LOAN', N'Loan'),
                (N'RENT', N'Rent'),
                (N'GIFT', N'Gift'),
                (N'MISC', N'Misc'),
                (N'RECEIPT', N'Receipt'),
                (N'INTERNAL_TRANSFER', N'Internal Transfer'),
                (N'TRANSFER', N'Transfer'),
                (N'NOT_SPECIFIED', N'Not Specified'),
                (N'VISA', N'Visa'),
                (N'CASH', N'Cash'),
                (N'MASTER_CARD', N'Master Card'),
                (N'DEBIT_CARD', N'Debit Card'),
                (N'AMERICAN_EXPRESS', N'American Express')
            ) seed(Code, Name)
            WHERE NOT EXISTS (
                SELECT 1 FROM dbo.AccountsTransTypes existing
                WHERE existing.Code = seed.Code AND existing.TenantId IS NULL
            );

            INSERT INTO dbo.AccountsTransModes (TenantId, Code, Name, IsActive)
            SELECT NULL, seed.Code, seed.Name, 1
            FROM (VALUES
                (N'CASH', N'Cash'),
                (N'CHEQUE', N'Cheque'),
                (N'DRAFT', N'Draft'),
                (N'TT', N'TT (Telegraph Transfer)'),
                (N'TRANSFER', N'Transfer'),
                (N'OTHER', N'Other'),
                (N'DEBIT_CARD', N'Debit Card'),
                (N'CREDIT_CARD', N'Credit Card')
            ) seed(Code, Name)
            WHERE NOT EXISTS (
                SELECT 1 FROM dbo.AccountsTransModes existing
                WHERE existing.Code = seed.Code AND existing.TenantId IS NULL
            );

            IF NOT EXISTS (SELECT 1 FROM dbo.AccountsTaxTypes WHERE TenantId IS NULL AND Code = N'GST')
                INSERT INTO dbo.AccountsTaxTypes (TenantId, Code, Name, DefaultRate, IsActive)
                VALUES (NULL, N'GST', N'GST', CAST(0 AS decimal(18,4)), 1);

            IF NOT EXISTS (SELECT 1 FROM dbo.AccountsEntryStatuses WHERE TenantId IS NULL AND Code = N'ENTERED')
                INSERT INTO dbo.AccountsEntryStatuses (TenantId, Code, Name, ColorCode, FontColor, IsActive)
                VALUES (NULL, N'ENTERED', N'Entered', N'#0284c7', N'#ffffff', 1);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Shared lookup rows are intentionally retained because saved transactions may reference them.
    }
}
