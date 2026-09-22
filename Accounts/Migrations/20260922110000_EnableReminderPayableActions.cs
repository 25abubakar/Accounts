using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

/// <summary>
/// Adds the Payable CRUD feature keys and enables them in each tenant's
/// menu ceiling. The original Reminders menu migration only enabled View,
/// which caused the shared GridHeader to hide its add/form button.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260922110000_EnableReminderPayableActions")]
public sealed class EnableReminderPayableActions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            SET NOCOUNT ON;

            DECLARE @PayableMenuId int = (
                SELECT TOP (1) Id
                FROM dbo.Menus
                WHERE Route = N'/reminders/payable'
                ORDER BY Id
            );

            IF @PayableMenuId IS NOT NULL
            BEGIN
                INSERT INTO dbo.Features (FeatureKey, FeatureName, Module, Description, CreatedDate)
                SELECT source.FeatureKey, source.FeatureName, N'Menu', source.Description, SYSUTCDATETIME()
                FROM (VALUES
                    (CONCAT(N'MENU_', @PayableMenuId, N'_ADD'),    N'Payable - Add',    N'Create payable reminders.'),
                    (CONCAT(N'MENU_', @PayableMenuId, N'_EDIT'),   N'Payable - Edit',   N'Edit payable reminders.'),
                    (CONCAT(N'MENU_', @PayableMenuId, N'_DELETE'), N'Payable - Delete', N'Delete payable reminders.')
                ) source(FeatureKey, FeatureName, Description)
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM dbo.Features existing
                    WHERE existing.FeatureKey = source.FeatureKey
                );

                INSERT INTO dbo.MenuPermissions (MenuId, PermissionId)
                SELECT @PayableMenuId, feature.PermissionId
                FROM dbo.Features feature
                WHERE feature.FeatureKey IN (
                    CONCAT(N'MENU_', @PayableMenuId, N'_ADD'),
                    CONCAT(N'MENU_', @PayableMenuId, N'_EDIT'),
                    CONCAT(N'MENU_', @PayableMenuId, N'_DELETE'))
                  AND NOT EXISTS (
                      SELECT 1
                      FROM dbo.MenuPermissions existing
                      WHERE existing.MenuId = @PayableMenuId
                        AND existing.PermissionId = feature.PermissionId
                  );

                UPDATE dbo.TenantMenuPermissions
                SET IsAllow = 1,
                    CanView = 1,
                    CanAdd = 1,
                    CanEdit = 1,
                    CanDelete = 1
                WHERE MenuId = @PayableMenuId;

                INSERT INTO dbo.TenantMenuPermissions
                    (TenantId, MenuId, IsAllow, CanView, CanAdd, CanEdit, CanDelete, GrantedOnUtc, GrantedByUserId)
                SELECT tenant.Id, @PayableMenuId, 1, 1, 1, 1, 1,
                       SYSUTCDATETIME(), N'System: Payable actions'
                FROM dbo.Tenants tenant
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM dbo.TenantMenuPermissions existing
                    WHERE existing.TenantId = tenant.Id
                      AND existing.MenuId = @PayableMenuId
                );
            END;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            SET NOCOUNT ON;

            DECLARE @PayableMenuId int = (
                SELECT TOP (1) Id
                FROM dbo.Menus
                WHERE Route = N'/reminders/payable'
                ORDER BY Id
            );

            IF @PayableMenuId IS NOT NULL
            BEGIN
                UPDATE dbo.TenantMenuPermissions
                SET CanAdd = 0, CanEdit = 0, CanDelete = 0
                WHERE MenuId = @PayableMenuId;

                DELETE menuPermission
                FROM dbo.MenuPermissions menuPermission
                JOIN dbo.Features feature ON feature.PermissionId = menuPermission.PermissionId
                WHERE menuPermission.MenuId = @PayableMenuId
                  AND feature.FeatureKey IN (
                      CONCAT(N'MENU_', @PayableMenuId, N'_ADD'),
                      CONCAT(N'MENU_', @PayableMenuId, N'_EDIT'),
                      CONCAT(N'MENU_', @PayableMenuId, N'_DELETE'));
            END;
            """);
    }
}
