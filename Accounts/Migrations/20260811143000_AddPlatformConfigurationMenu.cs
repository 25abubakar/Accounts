using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260811143000_AddPlatformConfigurationMenu")]
public sealed class AddPlatformConfigurationMenu : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DECLARE @PlatformId int = (
                SELECT TOP (1) Id
                FROM dbo.Menus
                WHERE ParentId IS NULL AND Title IN (N'Platform Settings', N'Settings')
                ORDER BY CASE WHEN Title = N'Platform Settings' THEN 0 ELSE 1 END, Id
            );

            IF @PlatformId IS NULL
                THROW 51000, 'Platform Settings menu was not found.', 1;

            DECLARE @SettingsId int = (
                SELECT TOP (1) Id
                FROM dbo.Menus
                WHERE Route = N'/settings/configuration'
                ORDER BY Id
            );

            IF @SettingsId IS NULL
            BEGIN
                INSERT INTO dbo.Menus (Title, Icon, Route, ParentId, SortOrder, IsActive)
                VALUES (N'Settings', N'SlidersHorizontal', N'/settings/configuration', @PlatformId, 10, 1);
                SET @SettingsId = SCOPE_IDENTITY();
            END
            ELSE
            BEGIN
                UPDATE dbo.Menus
                SET Title = N'Settings',
                    Icon = N'SlidersHorizontal',
                    ParentId = @PlatformId,
                    SortOrder = 10,
                    IsActive = 1
                WHERE Id = @SettingsId;
            END;

            -- Make the new screen available wherever the existing Types screen
            -- is available. Fine-grained staff access remains managed by the
            -- existing access-control screens.
            DECLARE @SourceMenuId int = (
                SELECT TOP (1) Id
                FROM dbo.Menus
                WHERE Route IN (N'/settings/types', N'/settings/statuses')
                ORDER BY CASE WHEN Route = N'/settings/types' THEN 0 ELSE 1 END, Id
            );

            INSERT INTO dbo.TenantMenuPermissions
                (TenantId, MenuId, IsAllow, CanView, CanAdd, CanEdit, CanDelete, GrantedOnUtc, GrantedByUserId)
            SELECT tenant.Id,
                   @SettingsId,
                   COALESCE(sourcePermission.IsAllow, 1),
                   COALESCE(sourcePermission.CanView, 1),
                   0,
                   0,
                   0,
                   SYSUTCDATETIME(),
                   N'System: Platform Settings configuration menu'
            FROM dbo.Tenants tenant
            LEFT JOIN dbo.TenantMenuPermissions sourcePermission
              ON sourcePermission.TenantId = tenant.Id
             AND sourcePermission.MenuId = @SourceMenuId
            WHERE NOT EXISTS (
                SELECT 1
                FROM dbo.TenantMenuPermissions existingPermission
                WHERE existingPermission.TenantId = tenant.Id
                  AND existingPermission.MenuId = @SettingsId
            );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DECLARE @SettingsId int = (
                SELECT TOP (1) Id
                FROM dbo.Menus
                WHERE Route = N'/settings/configuration'
                ORDER BY Id
            );

            IF @SettingsId IS NOT NULL
            BEGIN
                DELETE featureAccess
                FROM dbo.AccessFeatures featureAccess
                JOIN dbo.StaffMenuAccess staffAccess
                  ON staffAccess.Id = featureAccess.StaffMenuAccessId
                WHERE staffAccess.MenuId = @SettingsId;

                DELETE FROM dbo.StaffMenuAccess WHERE MenuId = @SettingsId;
                DELETE FROM dbo.TenantMenuPermissions WHERE MenuId = @SettingsId;
                DELETE FROM dbo.MenuPermissions WHERE MenuId = @SettingsId;
                DELETE FROM dbo.Menus WHERE Id = @SettingsId;
            END;
            """);
    }
}
