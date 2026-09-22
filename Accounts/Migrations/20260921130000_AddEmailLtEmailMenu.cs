using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

/// <summary>
/// Adds the E-mail parent menu and its LT Email child to the database-driven sidebar.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260921130000_AddEmailLtEmailMenu")]
public sealed class AddEmailLtEmailMenu : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            SET NOCOUNT ON;

            DECLARE @EmailParentId int = (
                SELECT TOP (1) Id
                FROM dbo.Menus
                WHERE ParentId IS NULL
                  AND (Title = N'E-mail' OR Title = N'Email')
                ORDER BY CASE WHEN Title = N'E-mail' THEN 0 ELSE 1 END, Id
            );

            IF @EmailParentId IS NULL
            BEGIN
                INSERT INTO dbo.Menus (Title, Icon, Route, ParentId, SortOrder, IsActive)
                VALUES (N'E-mail', N'Mail', NULL, NULL, 82, 1);
                SET @EmailParentId = SCOPE_IDENTITY();
            END;
            ELSE
                UPDATE dbo.Menus
                SET Title = N'E-mail', Icon = N'Mail', Route = NULL,
                    ParentId = NULL, SortOrder = 82, IsActive = 1
                WHERE Id = @EmailParentId;

            DECLARE @LtEmailId int = (
                SELECT TOP (1) Id
                FROM dbo.Menus
                WHERE Route = N'/email/lt-email'
                   OR (ParentId = @EmailParentId AND Title = N'LT Email')
                ORDER BY CASE WHEN Route = N'/email/lt-email' THEN 0 ELSE 1 END, Id
            );

            IF @LtEmailId IS NULL
            BEGIN
                INSERT INTO dbo.Menus (Title, Icon, Route, ParentId, SortOrder, IsActive)
                VALUES (N'LT Email', N'MailOpen', N'/email/lt-email', @EmailParentId, 1, 1);
                SET @LtEmailId = SCOPE_IDENTITY();
            END;
            ELSE
                UPDATE dbo.Menus
                SET Title = N'LT Email', Icon = N'MailOpen', Route = N'/email/lt-email',
                    ParentId = @EmailParentId, SortOrder = 1, IsActive = 1
                WHERE Id = @LtEmailId;

            DECLARE @MenuFeatureKey nvarchar(100) = CONCAT(N'MENU_', @LtEmailId);
            DECLARE @ViewFeatureKey nvarchar(100) = CONCAT(N'MENU_', @LtEmailId, N'_VIEW');

            IF NOT EXISTS (SELECT 1 FROM dbo.Features WHERE FeatureKey = @MenuFeatureKey)
                INSERT INTO dbo.Features (FeatureKey, FeatureName, Module, Description, CreatedDate)
                VALUES (@MenuFeatureKey, N'LT Email', N'Menu', N'Open the LT Email screen.', SYSUTCDATETIME());

            IF NOT EXISTS (SELECT 1 FROM dbo.Features WHERE FeatureKey = @ViewFeatureKey)
                INSERT INTO dbo.Features (FeatureKey, FeatureName, Module, Description, CreatedDate)
                VALUES (@ViewFeatureKey, N'LT Email - View', N'Menu', N'View the LT Email screen.', SYSUTCDATETIME());

            INSERT INTO dbo.MenuPermissions (MenuId, PermissionId)
            SELECT @LtEmailId, feature.PermissionId
            FROM dbo.Features feature
            WHERE feature.FeatureKey IN (@MenuFeatureKey, @ViewFeatureKey)
              AND NOT EXISTS (
                  SELECT 1 FROM dbo.MenuPermissions existing
                  WHERE existing.MenuId = @LtEmailId
                    AND existing.PermissionId = feature.PermissionId
              );

            -- Put the new screen inside every existing tenant's permission ceiling.
            -- Tenant admins can see it immediately; regular staff still receive it
            -- through the existing role/person access screens without changing their grants.
            INSERT INTO dbo.TenantMenuPermissions
                (TenantId, MenuId, IsAllow, CanView, CanAdd, CanEdit, CanDelete, GrantedOnUtc, GrantedByUserId)
            SELECT tenant.Id, @LtEmailId, 1, 1, 0, 0, 0,
                   SYSUTCDATETIME(), N'System: LT Email menu'
            FROM dbo.Tenants tenant
            WHERE NOT EXISTS (
                SELECT 1 FROM dbo.TenantMenuPermissions existing
                WHERE existing.TenantId = tenant.Id
                  AND existing.MenuId = @LtEmailId
            );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            SET NOCOUNT ON;

            DECLARE @LtEmailId int = (
                SELECT TOP (1) Id FROM dbo.Menus
                WHERE Route = N'/email/lt-email'
                ORDER BY Id
            );

            IF @LtEmailId IS NOT NULL
            BEGIN
                DECLARE @Features table (PermissionId int NOT NULL);
                INSERT INTO @Features (PermissionId)
                SELECT PermissionId FROM dbo.Features
                WHERE FeatureKey IN (
                    CONCAT(N'MENU_', @LtEmailId),
                    CONCAT(N'MENU_', @LtEmailId, N'_VIEW'));

                DELETE accessFeature
                FROM dbo.AccessFeatures accessFeature
                JOIN dbo.StaffMenuAccess access ON access.Id = accessFeature.StaffMenuAccessId
                WHERE access.MenuId = @LtEmailId;

                DELETE personFeature FROM dbo.PersonFeatures personFeature
                JOIN @Features feature ON feature.PermissionId = personFeature.PermissionId;
                DELETE rolePermission FROM dbo.TenantRolePermissions rolePermission
                JOIN @Features feature ON feature.PermissionId = rolePermission.PermissionId;
                DELETE rolePermission FROM dbo.RolePermissions rolePermission
                JOIN @Features feature ON feature.PermissionId = rolePermission.PermissionId;
                DELETE matrixPermission FROM dbo.DepartmentAccessMatrix matrixPermission
                JOIN @Features feature ON feature.PermissionId = matrixPermission.PermissionId;
                DELETE FROM dbo.MenuPermissions WHERE MenuId = @LtEmailId;
                DELETE FROM dbo.StaffMenuAccess WHERE MenuId = @LtEmailId;
                DELETE FROM dbo.TenantMenuPermissions WHERE MenuId = @LtEmailId;
                DELETE FROM dbo.Menus WHERE Id = @LtEmailId;

                DELETE featureRow FROM dbo.Features featureRow
                JOIN @Features feature ON feature.PermissionId = featureRow.PermissionId;
            END;

            DELETE parentMenu
            FROM dbo.Menus parentMenu
            WHERE parentMenu.ParentId IS NULL
              AND parentMenu.Title = N'E-mail'
              AND NOT EXISTS (SELECT 1 FROM dbo.Menus child WHERE child.ParentId = parentMenu.Id);
            """);
    }
}
