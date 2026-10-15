using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

/// <summary>
/// Makes E-Marketing the canonical Sale Roznamcha child route while retaining
/// the existing menu row (and therefore all MenuId-based RBAC assignments).
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20261014250000_RenameSaleRoznamchaManagementRouteToEMarketing")]
public sealed class RenameSaleRoznamchaManagementRouteToEMarketing : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            SET NOCOUNT ON;

            UPDATE dbo.Menus
            SET Title = N'E-Marketing',
                Route = N'/sale-roznamcha/e-marketing'
            WHERE Route = N'/sale-roznamcha/management';

            UPDATE feature
            SET FeatureName = REPLACE(feature.FeatureName, N'Management', N'E-Marketing'),
                Description = REPLACE(feature.Description, N'Management', N'E-Marketing')
            FROM dbo.Features feature
            INNER JOIN dbo.Menus menu
                ON feature.FeatureKey LIKE CONCAT(N'MENU_', menu.Id, N'%')
            WHERE menu.Route = N'/sale-roznamcha/e-marketing';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            SET NOCOUNT ON;

            UPDATE dbo.Menus
            SET Route = N'/sale-roznamcha/management'
            WHERE Route = N'/sale-roznamcha/e-marketing'
              AND Title = N'E-Marketing';
            """);
    }
}
