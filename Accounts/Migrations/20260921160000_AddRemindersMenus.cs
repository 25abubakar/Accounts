using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

/// <summary>
/// Adds the Reminders parent menu and Receivable / Payable children
/// to the database-driven sidebar, with tenant view grants.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260921160000_AddRemindersMenus")]
public sealed class AddRemindersMenus : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            SET NOCOUNT ON;

            DECLARE @RemindersParentId int = (
                SELECT TOP (1) Id
                FROM dbo.Menus
                WHERE ParentId IS NULL AND Title = N'Reminders'
                ORDER BY Id
            );

            IF @RemindersParentId IS NULL
            BEGIN
                INSERT INTO dbo.Menus (Title, Icon, Route, ParentId, SortOrder, IsActive)
                VALUES (N'Reminders', N'Bell', NULL, NULL, 83, 1);
                SET @RemindersParentId = SCOPE_IDENTITY();
            END;
            ELSE
                UPDATE dbo.Menus
                SET Title = N'Reminders', Icon = N'Bell', Route = NULL,
                    ParentId = NULL, SortOrder = 83, IsActive = 1
                WHERE Id = @RemindersParentId;

            DECLARE @ReceivableId int = (
                SELECT TOP (1) Id
                FROM dbo.Menus
                WHERE Route = N'/reminders/receivable'
                   OR (ParentId = @RemindersParentId AND Title IN (N'Receivable', N'Reciveable'))
                ORDER BY CASE WHEN Route = N'/reminders/receivable' THEN 0 ELSE 1 END, Id
            );

            IF @ReceivableId IS NULL
            BEGIN
                INSERT INTO dbo.Menus (Title, Icon, Route, ParentId, SortOrder, IsActive)
                VALUES (N'Receivable', N'Wallet', N'/reminders/receivable', @RemindersParentId, 1, 1);
                SET @ReceivableId = SCOPE_IDENTITY();
            END;
            ELSE
                UPDATE dbo.Menus
                SET Title = N'Receivable', Icon = N'Wallet', Route = N'/reminders/receivable',
                    ParentId = @RemindersParentId, SortOrder = 1, IsActive = 1
                WHERE Id = @ReceivableId;

            DECLARE @PayableId int = (
                SELECT TOP (1) Id
                FROM dbo.Menus
                WHERE Route = N'/reminders/payable'
                   OR (ParentId = @RemindersParentId AND Title = N'Payable')
                ORDER BY CASE WHEN Route = N'/reminders/payable' THEN 0 ELSE 1 END, Id
            );

            IF @PayableId IS NULL
            BEGIN
                INSERT INTO dbo.Menus (Title, Icon, Route, ParentId, SortOrder, IsActive)
                VALUES (N'Payable', N'Banknote', N'/reminders/payable', @RemindersParentId, 2, 1);
                SET @PayableId = SCOPE_IDENTITY();
            END;
            ELSE
                UPDATE dbo.Menus
                SET Title = N'Payable', Icon = N'Banknote', Route = N'/reminders/payable',
                    ParentId = @RemindersParentId, SortOrder = 2, IsActive = 1
                WHERE Id = @PayableId;

            DECLARE @Targets table (MenuId int NOT NULL, Title nvarchar(100) NOT NULL);
            INSERT INTO @Targets (MenuId, Title)
            VALUES
                (@ReceivableId, N'Receivable'),
                (@PayableId, N'Payable');

            INSERT INTO dbo.Features (FeatureKey, FeatureName, Module, Description, CreatedDate)
            SELECT CONCAT(N'MENU_', target.MenuId), target.Title, N'Menu',
                   CONCAT(N'Open the ', target.Title, N' screen.'), SYSUTCDATETIME()
            FROM @Targets target
            WHERE NOT EXISTS (
                SELECT 1 FROM dbo.Features feature
                WHERE feature.FeatureKey = CONCAT(N'MENU_', target.MenuId)
            );

            INSERT INTO dbo.Features (FeatureKey, FeatureName, Module, Description, CreatedDate)
            SELECT CONCAT(N'MENU_', target.MenuId, N'_VIEW'), CONCAT(target.Title, N' - View'), N'Menu',
                   CONCAT(N'View the ', target.Title, N' screen.'), SYSUTCDATETIME()
            FROM @Targets target
            WHERE NOT EXISTS (
                SELECT 1 FROM dbo.Features feature
                WHERE feature.FeatureKey = CONCAT(N'MENU_', target.MenuId, N'_VIEW')
            );

            INSERT INTO dbo.MenuPermissions (MenuId, PermissionId)
            SELECT target.MenuId, feature.PermissionId
            FROM @Targets target
            JOIN dbo.Features feature
              ON feature.FeatureKey IN (
                    CONCAT(N'MENU_', target.MenuId),
                    CONCAT(N'MENU_', target.MenuId, N'_VIEW'))
            WHERE NOT EXISTS (
                SELECT 1 FROM dbo.MenuPermissions existing
                WHERE existing.MenuId = target.MenuId
                  AND existing.PermissionId = feature.PermissionId
            );

            INSERT INTO dbo.TenantMenuPermissions
                (TenantId, MenuId, IsAllow, CanView, CanAdd, CanEdit, CanDelete, GrantedOnUtc, GrantedByUserId)
            SELECT tenant.Id, menuGrant.MenuId, 1, 1, 0, 0, 0,
                   SYSUTCDATETIME(), N'System: Reminders menus'
            FROM dbo.Tenants tenant
            CROSS JOIN (
                SELECT @RemindersParentId AS MenuId
                UNION ALL
                SELECT MenuId FROM @Targets
            ) menuGrant
            WHERE NOT EXISTS (
                SELECT 1 FROM dbo.TenantMenuPermissions existing
                WHERE existing.TenantId = tenant.Id
                  AND existing.MenuId = menuGrant.MenuId
            );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            SET NOCOUNT ON;

            DECLARE @Targets table (MenuId int NOT NULL);
            INSERT INTO @Targets (MenuId)
            SELECT Id FROM dbo.Menus
            WHERE Route IN (N'/reminders/receivable', N'/reminders/payable')
               OR (ParentId IS NULL AND Title = N'Reminders');

            DECLARE @Features table (PermissionId int NOT NULL);
            INSERT INTO @Features (PermissionId)
            SELECT feature.PermissionId
            FROM dbo.Features feature
            JOIN @Targets target
              ON feature.FeatureKey IN (
                    CONCAT(N'MENU_', target.MenuId),
                    CONCAT(N'MENU_', target.MenuId, N'_VIEW'));

            DELETE accessFeature
            FROM dbo.AccessFeatures accessFeature
            JOIN dbo.StaffMenuAccess access ON access.Id = accessFeature.StaffMenuAccessId
            JOIN @Targets target ON target.MenuId = access.MenuId;

            DELETE personFeature FROM dbo.PersonFeatures personFeature
            JOIN @Features feature ON feature.PermissionId = personFeature.PermissionId;
            DELETE rolePermission FROM dbo.TenantRolePermissions rolePermission
            JOIN @Features feature ON feature.PermissionId = rolePermission.PermissionId;
            DELETE rolePermission FROM dbo.RolePermissions rolePermission
            JOIN @Features feature ON feature.PermissionId = rolePermission.PermissionId;
            DELETE matrixPermission FROM dbo.DepartmentAccessMatrix matrixPermission
            JOIN @Features feature ON feature.PermissionId = matrixPermission.PermissionId;
            DELETE menuPermission FROM dbo.MenuPermissions menuPermission
            JOIN @Targets target ON target.MenuId = menuPermission.MenuId;
            DELETE staffAccess FROM dbo.StaffMenuAccess staffAccess
            JOIN @Targets target ON target.MenuId = staffAccess.MenuId;
            DELETE tenantPermission FROM dbo.TenantMenuPermissions tenantPermission
            JOIN @Targets target ON target.MenuId = tenantPermission.MenuId;
            DELETE childMenu FROM dbo.Menus childMenu
            JOIN @Targets target ON target.MenuId = childMenu.Id
            WHERE childMenu.ParentId IS NOT NULL;
            DELETE parentMenu FROM dbo.Menus parentMenu
            JOIN @Targets target ON target.MenuId = parentMenu.Id
            WHERE parentMenu.ParentId IS NULL
              AND NOT EXISTS (SELECT 1 FROM dbo.Menus child WHERE child.ParentId = parentMenu.Id);

            DELETE featureRow FROM dbo.Features featureRow
            JOIN @Features feature ON feature.PermissionId = featureRow.PermissionId;
            """);
    }
}
