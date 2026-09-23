using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

/// <summary>
/// Seeds Category Type dropdown values (Official, Per, Misc) and backfills LT-Cat-{Id} refs.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260923142000_SeedAccountCategoryTypes")]
public sealed class SeedAccountCategoryTypes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            SET NOCOUNT ON;

            INSERT INTO dbo.AccountsCategoryTypes (TenantId, Name, IsActive, CreatedOnUtc)
            SELECT t.Id, seed.Name, 1, SYSUTCDATETIME()
            FROM dbo.Tenants t
            CROSS JOIN (VALUES
                (N'Official'),
                (N'Per'),
                (N'Misc')
            ) seed(Name)
            WHERE NOT EXISTS (
                SELECT 1
                FROM dbo.AccountsCategoryTypes existing
                WHERE existing.TenantId = t.Id AND existing.Name = seed.Name
            );

            UPDATE dbo.AccountsCategories
            SET ReferenceNumber = CONCAT(N'LT-Cat-', Id)
            WHERE ReferenceNumber IS NULL OR LTRIM(RTRIM(ReferenceNumber)) = N'';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DELETE FROM dbo.AccountsCategoryTypes
            WHERE Name IN (N'Official', N'Per', N'Misc')
              AND NOT EXISTS (
                  SELECT 1 FROM dbo.AccountsCategories c
                  WHERE c.CategoryTypeId = AccountsCategoryTypes.Id
              );
            """);
    }
}
