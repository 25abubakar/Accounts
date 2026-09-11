using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

/// <summary>
/// Align PayrollTaxSlabs with progressive Pakistan-style bands and correct Slab-4 fixed tax (116,000).
/// Uses temporary FromAmount values first to avoid unique index (TenantId, TaxYear, FromAmount) conflicts.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260909190000_FixPayrollTaxSlabProgressiveAmounts")]
public sealed class FixPayrollTaxSlabProgressiveAmounts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            -- Progressive structure (annual):
            -- <= 600,000: 0%
            -- 600,000–1,200,000: Fixed 0 + 1% on excess over 600,000
            -- 1,200,000–2,200,000: Fixed 6,000 + 11% on excess over 1,200,000
            -- 2,200,000–3,200,000: Fixed 116,000 + 23% on excess over 2,200,000
            -- 3,200,000–4,100,000: Fixed 346,000 + 30% on excess over 3,200,000
            -- > 4,100,000: Fixed 616,000 + 35% on excess over 4,100,000

            ;WITH Ordered AS (
                SELECT Id, ROW_NUMBER() OVER (ORDER BY FromAmount, Id) AS Rn
                FROM dbo.PayrollTaxSlabs
                WHERE TaxYear = N'2026-2027'
            )
            UPDATE s SET
                -- Negative temps keep unique (TenantId, TaxYear, FromAmount); DESC restore keeps original Rn order.
                FromAmount = -o.Rn,
                UpdatedOnUtc = SYSUTCDATETIME()
            FROM dbo.PayrollTaxSlabs s
            INNER JOIN Ordered o ON o.Id = s.Id;

            ;WITH Ordered AS (
                SELECT Id, ROW_NUMBER() OVER (ORDER BY FromAmount DESC, Id) AS Rn
                FROM dbo.PayrollTaxSlabs
                WHERE TaxYear = N'2026-2027'
            )
            UPDATE s SET
                SlabName = CASE o.Rn
                    WHEN 1 THEN N'Slab 1'
                    WHEN 2 THEN N'Slab 2'
                    WHEN 3 THEN N'Slab 3'
                    WHEN 4 THEN N'Slab 4'
                    WHEN 5 THEN N'Slab 5'
                    WHEN 6 THEN N'Slab 6'
                    ELSE s.SlabName END,
                FromAmount = CASE o.Rn
                    WHEN 1 THEN 0
                    WHEN 2 THEN 600000
                    WHEN 3 THEN 1200000
                    WHEN 4 THEN 2200000
                    WHEN 5 THEN 3200000
                    WHEN 6 THEN 4100000
                    ELSE s.FromAmount END,
                ToAmount = CASE o.Rn
                    WHEN 1 THEN 600000
                    WHEN 2 THEN 1200000
                    WHEN 3 THEN 2200000
                    WHEN 4 THEN 3200000
                    WHEN 5 THEN 4100000
                    WHEN 6 THEN NULL
                    ELSE s.ToAmount END,
                FixedTaxAmount = CASE o.Rn
                    WHEN 1 THEN 0
                    WHEN 2 THEN 0
                    WHEN 3 THEN 6000
                    WHEN 4 THEN 116000
                    WHEN 5 THEN 346000
                    WHEN 6 THEN 616000
                    ELSE s.FixedTaxAmount END,
                RatePercentage = CASE o.Rn
                    WHEN 1 THEN 0
                    WHEN 2 THEN 1
                    WHEN 3 THEN 11
                    WHEN 4 THEN 23
                    WHEN 5 THEN 30
                    WHEN 6 THEN 35
                    ELSE s.RatePercentage END,
                RateType = CASE WHEN o.Rn = 1 THEN N'Fixed' ELSE N'Percentage' END,
                TotTax = CASE o.Rn
                    WHEN 1 THEN 0
                    WHEN 2 THEN 0
                    WHEN 3 THEN 6000
                    WHEN 4 THEN 116000
                    WHEN 5 THEN 346000
                    WHEN 6 THEN 616000
                    ELSE s.TotTax END,
                IsActive = 1,
                UpdatedOnUtc = SYSUTCDATETIME()
            FROM dbo.PayrollTaxSlabs s
            INNER JOIN Ordered o ON o.Id = s.Id;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Non-destructive: leave corrected progressive amounts in place.
    }
}
