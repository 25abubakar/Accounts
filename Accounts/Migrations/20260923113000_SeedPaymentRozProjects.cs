using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260923113000_SeedPaymentRozProjects")]
public sealed class SeedPaymentRozProjects : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            INSERT INTO dbo.AccountsProjects (TenantId, Name, IsActive)
            SELECT tenant.Id, project.Name, 1
            FROM dbo.Tenants tenant
            CROSS JOIN (VALUES
                (N'Med Svc'),
                (N'E Marketing')
            ) project(Name)
            WHERE tenant.IsActive = 1
              AND NOT EXISTS (
                  SELECT 1
                  FROM dbo.AccountsProjects existing
                  WHERE existing.TenantId = tenant.Id
                    AND existing.Name = project.Name
              );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Project rows are retained because saved transactions may reference them.
    }
}
