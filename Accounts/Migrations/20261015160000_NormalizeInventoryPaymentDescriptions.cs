using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

/// <summary>
/// Keeps the entered purchase text in Description, moves the transaction nature
/// to Remarks, and removes the old generated prefix from linked payment rows.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20261015160000_NormalizeInventoryPaymentDescriptions")]
public sealed class NormalizeInventoryPaymentDescriptions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(ExecutableBatch(SaleRoznamchaSql.PostInventoryPurchasePaymentProcedure));
        migrationBuilder.Sql(
            """
            DECLARE @InventoryPrefix nvarchar(50) = CONCAT(N'Inventory purchase ', NCHAR(8212), N' ');

            UPDATE entry
            SET entry.Descriptions = NULLIF(
                    LTRIM(RTRIM(SUBSTRING(entry.Descriptions, LEN(@InventoryPrefix) + 1, 2000))),
                    N''),
                entry.Remarks = COALESCE(NULLIF(LTRIM(RTRIM(entry.Remarks)), N''), N'Inventory purchase')
            FROM dbo.RoznamchaEntries entry
            INNER JOIN dbo.PaymentRoznamcha purchase
                ON purchase.TenantId = entry.TenantId
               AND purchase.RoznamchaEntryId = entry.Id
            WHERE LEFT(entry.Descriptions, LEN(@InventoryPrefix)) = @InventoryPrefix;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Cleaned user-facing descriptions cannot be reconstructed without inventing text.
    }

    private static string ExecutableBatch(string sql) => $"EXEC(N'{sql.Replace("'", "''")}');";
}
