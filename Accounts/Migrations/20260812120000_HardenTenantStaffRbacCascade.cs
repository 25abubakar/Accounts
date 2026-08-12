using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

/// <summary>
/// Hardens Super Admin → Tenant Admin → Staff RBAC:
/// 1) Ensures MENU_{id}_VIEW/ADD/EDIT/DELETE features exist and map to menus.
/// 2) Prunes staff grants that exceed the current tenant ceiling.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260812120000_HardenTenantStaffRbacCascade")]
public sealed class HardenTenantStaffRbacCascade : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ;WITH MenuCrud AS
            (
                SELECT
                    m.Id AS MenuId,
                    m.Title AS MenuTitle,
                    suffix.ActionCode,
                    suffix.ActionName
                FROM dbo.Menus m
                CROSS JOIN (VALUES
                    (N'VIEW', N'View'),
                    (N'ADD', N'Add'),
                    (N'EDIT', N'Edit'),
                    (N'DELETE', N'Delete')
                ) suffix(ActionCode, ActionName)
                WHERE m.IsActive = 1
            )
            MERGE dbo.Features AS target
            USING
            (
                SELECT
                    N'MENU_' + CAST(MenuId AS nvarchar(20)) + N'_' + ActionCode AS FeatureKey,
                    MenuTitle + N' - ' + ActionName AS FeatureName
                FROM MenuCrud
            ) AS source
            ON target.FeatureKey = source.FeatureKey
            WHEN NOT MATCHED THEN
                INSERT (FeatureKey, FeatureName, Module)
                VALUES (source.FeatureKey, source.FeatureName, N'Menu');

            INSERT INTO dbo.MenuPermissions (MenuId, PermissionId)
            SELECT m.Id, f.PermissionId
            FROM dbo.Menus m
            JOIN dbo.Features f
              ON f.FeatureKey = N'MENU_' + CAST(m.Id AS nvarchar(20))
              OR f.FeatureKey LIKE N'MENU_' + CAST(m.Id AS nvarchar(20)) + N'_%'
            WHERE NOT EXISTS
            (
                SELECT 1 FROM dbo.MenuPermissions mp
                WHERE mp.MenuId = m.Id AND mp.PermissionId = f.PermissionId
            );

            DELETE sma
            FROM dbo.StaffMenuAccess sma
            JOIN dbo.StaffVacancy sv ON sv.StaffId = sma.StaffId
            WHERE NOT EXISTS
            (
                SELECT 1
                FROM dbo.TenantMenuPermissions tmp
                WHERE tmp.TenantId = sv.TenantId
                  AND tmp.MenuId = sma.MenuId
                  AND tmp.IsAllow = 1
                  AND tmp.CanView = 1
            );

            DELETE af
            FROM dbo.AccessFeatures af
            JOIN dbo.StaffMenuAccess sma ON sma.Id = af.StaffMenuAccessId
            JOIN dbo.StaffVacancy sv ON sv.StaffId = sma.StaffId
            JOIN dbo.Features f ON f.PermissionId = af.PermissionId
            LEFT JOIN dbo.TenantMenuPermissions tmp
              ON tmp.TenantId = sv.TenantId
             AND tmp.MenuId = sma.MenuId
             AND tmp.IsAllow = 1
            WHERE tmp.MenuId IS NULL
               OR (f.FeatureKey LIKE N'MENU_%_VIEW' AND tmp.CanView = 0)
               OR (f.FeatureKey LIKE N'MENU_%_ADD' AND tmp.CanAdd = 0)
               OR (f.FeatureKey LIKE N'MENU_%_EDIT' AND tmp.CanEdit = 0)
               OR (f.FeatureKey LIKE N'MENU_%_DELETE' AND tmp.CanDelete = 0);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Data prune is intentionally one-way.
    }
}
