using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

/// <summary>
/// Repairs the RBAC catalogue after the menu CRUD refactor. Every MENU_{id}
/// View/Add/Edit/Delete feature must be linked to its owning menu so the tenant
/// ceiling can authorize and persist those actions.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260804123000_RepairMenuCrudPermissionLinks")]
public sealed class RepairMenuCrudPermissionLinks : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            INSERT INTO dbo.MenuPermissions (MenuId, PermissionId)
            SELECT menu.Id, feature.PermissionId
            FROM dbo.Menus AS menu
            INNER JOIN dbo.Features AS feature
                ON feature.FeatureKey IN
                (
                    CONCAT(N'MENU_', menu.Id),
                    CONCAT(N'MENU_', menu.Id, N'_VIEW'),
                    CONCAT(N'MENU_', menu.Id, N'_ADD'),
                    CONCAT(N'MENU_', menu.Id, N'_EDIT'),
                    CONCAT(N'MENU_', menu.Id, N'_DELETE')
                )
            WHERE menu.IsActive = 1
              AND NOT EXISTS
              (
                  SELECT 1
                  FROM dbo.MenuPermissions AS existing
                  WHERE existing.MenuId = menu.Id
                    AND existing.PermissionId = feature.PermissionId
              );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // The links are required catalogue data and intentionally preserved.
    }
}
