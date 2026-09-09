using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260909070000_SeedBenefitTypesCatalog")]
public sealed class SeedBenefitTypesCatalog : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DECLARE @BenefitTypes TABLE
            (
                Name nvarchar(150) NOT NULL,
                Code nvarchar(100) NOT NULL,
                DisplayOrder int NOT NULL
            );

            INSERT @BenefitTypes (Name, Code, DisplayOrder) VALUES
                (N'EOBI', N'EOBI', 1),
                (N'Bonus', N'BONUS', 2),
                (N'Provident Fund', N'PROVIDENT_FUND', 3),
                (N'Gratuity', N'GRATUITY', 4),
                (N'Incentive', N'INCENTIVE', 5),
                (N'Entertainment', N'ENTERTAINMENT', 6),
                (N'Tpt Funding', N'TPT_FUNDING', 7),
                (N'Security', N'SECURITY', 8),
                (N'Loan', N'LOAN', 9),
                (N'Tax', N'TAX', 10),
                (N'Proficiency', N'PROFICIENCY', 11);

            UPDATE existing
            SET existing.Name = source.Name,
                existing.DisplayOrder = source.DisplayOrder,
                existing.IsActive = 1,
                existing.ModifiedOnUtc = SYSUTCDATETIME()
            FROM PlatformTypes.BenefitTypes existing
            JOIN @BenefitTypes source ON source.Code = existing.Code;

            INSERT PlatformTypes.BenefitTypes
                (TenantId, Name, Code, DisplayOrder, IsActive, CreatedOnUtc)
            SELECT tenant.Id, source.Name, source.Code, source.DisplayOrder, 1, SYSUTCDATETIME()
            FROM dbo.Tenants tenant
            CROSS JOIN @BenefitTypes source
            WHERE NOT EXISTS
            (
                SELECT 1
                FROM PlatformTypes.BenefitTypes existing
                WHERE existing.TenantId = tenant.Id
                  AND (existing.Code = source.Code OR existing.Name = source.Name)
            );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Preserve referenced business lookup values on rollback. A reviewed
        // data-cleanup migration may deactivate them if the catalog changes.
    }
}
