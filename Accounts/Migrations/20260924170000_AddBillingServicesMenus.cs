using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

/// <summary>
/// Adds the Billing Services parent and its operational/report child menus,
/// including action features and tenant menu ceilings.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260924170000_AddBillingServicesMenus")]
public sealed class AddBillingServicesMenus : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            SET NOCOUNT ON;
            SET ANSI_NULLS ON;
            SET QUOTED_IDENTIFIER ON;

            DECLARE @ParentId int = (
                SELECT TOP (1) Id FROM dbo.Menus
                WHERE ParentId IS NULL AND Title = N'Billing Services'
                ORDER BY Id
            );

            IF @ParentId IS NULL
            BEGIN
                INSERT INTO dbo.Menus (Title, Icon, Route, ParentId, SortOrder, IsActive)
                VALUES (N'Billing Services', N'Users', NULL, NULL, 89, 1);
                SET @ParentId = SCOPE_IDENTITY();
            END;
            ELSE
                UPDATE dbo.Menus
                SET Title = N'Billing Services', Icon = N'Users', Route = NULL,
                    ParentId = NULL, SortOrder = 89, IsActive = 1
                WHERE Id = @ParentId;

            DECLARE @Definitions table
            (
                Title nvarchar(100) NOT NULL,
                Icon nvarchar(50) NOT NULL,
                Route nvarchar(200) NOT NULL,
                SortOrder int NOT NULL
            );
            INSERT INTO @Definitions (Title, Icon, Route, SortOrder)
            VALUES
                (N'Re Process', N'GitCompareArrows', N'/billing-services/re-process', 1),
                (N'CPT''s Processed', N'BadgeCheck', N'/billing-services/cpts-processed', 2),
                (N'AR Report', N'BarChart3', N'/billing-services/ar-report', 3),
                (N'Call List', N'List', N'/billing-services/call-list', 4),
                (N'Closed List', N'ListChecks', N'/billing-services/closed-list', 5),
                (N'Master List', N'Files', N'/billing-services/master-list', 6),
                (N'Grp Codes', N'Tags', N'/billing-services/grp-codes', 7),
                (N'Mics Report', N'BarChart3', N'/billing-services/mics-report', 8),
                (N'Claim Master List', N'ReceiptText', N'/billing-services/claim-master-list', 9),
                (N'Status Rules', N'SlidersHorizontal', N'/billing-services/status-rules', 10),
                (N'Code Usage', N'FileCog', N'/billing-services/code-usage', 11);

            DECLARE @Targets table (MenuId int NOT NULL, Title nvarchar(100) NOT NULL);
            DECLARE @Title nvarchar(100), @Icon nvarchar(50), @Route nvarchar(200),
                    @SortOrder int, @MenuId int;
            DECLARE menu_cursor CURSOR LOCAL FAST_FORWARD FOR
                SELECT Title, Icon, Route, SortOrder FROM @Definitions ORDER BY SortOrder;

            OPEN menu_cursor;
            FETCH NEXT FROM menu_cursor INTO @Title, @Icon, @Route, @SortOrder;
            WHILE @@FETCH_STATUS = 0
            BEGIN
                SET @MenuId = (
                    SELECT TOP (1) Id FROM dbo.Menus
                    WHERE Route = @Route OR (ParentId = @ParentId AND Title = @Title)
                    ORDER BY CASE WHEN Route = @Route THEN 0 ELSE 1 END, Id
                );

                IF @MenuId IS NULL
                BEGIN
                    INSERT INTO dbo.Menus (Title, Icon, Route, ParentId, SortOrder, IsActive)
                    VALUES (@Title, @Icon, @Route, @ParentId, @SortOrder, 1);
                    SET @MenuId = SCOPE_IDENTITY();
                END;
                ELSE
                    UPDATE dbo.Menus
                    SET Title = @Title, Icon = @Icon, Route = @Route,
                        ParentId = @ParentId, SortOrder = @SortOrder, IsActive = 1
                    WHERE Id = @MenuId;

                INSERT INTO @Targets (MenuId, Title) VALUES (@MenuId, @Title);
                FETCH NEXT FROM menu_cursor INTO @Title, @Icon, @Route, @SortOrder;
            END;
            CLOSE menu_cursor;
            DEALLOCATE menu_cursor;

            INSERT INTO dbo.Features (FeatureKey, FeatureName, Module, Description, CreatedDate)
            SELECT CONCAT(N'MENU_', target.MenuId), target.Title, N'Menu',
                   CONCAT(N'Open the ', target.Title, N' screen.'), SYSUTCDATETIME()
            FROM @Targets target
            WHERE NOT EXISTS (
                SELECT 1 FROM dbo.Features feature
                WHERE feature.FeatureKey = CONCAT(N'MENU_', target.MenuId)
            );

            INSERT INTO dbo.Features (FeatureKey, FeatureName, Module, Description, CreatedDate)
            SELECT action.FeatureKey, action.FeatureName, N'Menu', action.Description, SYSUTCDATETIME()
            FROM @Targets target
            CROSS APPLY (VALUES
                (CONCAT(N'MENU_', target.MenuId, N'_VIEW'), CONCAT(target.Title, N' - View'), CONCAT(N'View ', target.Title, N' records.')),
                (CONCAT(N'MENU_', target.MenuId, N'_ADD'), CONCAT(target.Title, N' - Add'), CONCAT(N'Add ', target.Title, N' records.')),
                (CONCAT(N'MENU_', target.MenuId, N'_EDIT'), CONCAT(target.Title, N' - Edit'), CONCAT(N'Edit ', target.Title, N' records.')),
                (CONCAT(N'MENU_', target.MenuId, N'_DELETE'), CONCAT(target.Title, N' - Delete'), CONCAT(N'Delete ', target.Title, N' records.'))
            ) action(FeatureKey, FeatureName, Description)
            WHERE NOT EXISTS (
                SELECT 1 FROM dbo.Features existing WHERE existing.FeatureKey = action.FeatureKey
            );

            INSERT INTO dbo.MenuPermissions (MenuId, PermissionId)
            SELECT target.MenuId, feature.PermissionId
            FROM @Targets target
            JOIN dbo.Features feature ON feature.FeatureKey IN (
                CONCAT(N'MENU_', target.MenuId),
                CONCAT(N'MENU_', target.MenuId, N'_VIEW'),
                CONCAT(N'MENU_', target.MenuId, N'_ADD'),
                CONCAT(N'MENU_', target.MenuId, N'_EDIT'),
                CONCAT(N'MENU_', target.MenuId, N'_DELETE'))
            WHERE NOT EXISTS (
                SELECT 1 FROM dbo.MenuPermissions existing
                WHERE existing.MenuId = target.MenuId
                  AND existing.PermissionId = feature.PermissionId
            );

            DECLARE @MenuGrants table
            (
                MenuId int NOT NULL,
                CanAdd bit NOT NULL,
                CanEdit bit NOT NULL,
                CanDelete bit NOT NULL
            );
            INSERT INTO @MenuGrants (MenuId, CanAdd, CanEdit, CanDelete)
            VALUES (@ParentId, 0, 0, 0);
            INSERT INTO @MenuGrants (MenuId, CanAdd, CanEdit, CanDelete)
            SELECT MenuId, 1, 1, 1 FROM @Targets;

            INSERT INTO dbo.TenantMenuPermissions
                (TenantId, MenuId, IsAllow, CanView, CanAdd, CanEdit, CanDelete, GrantedOnUtc, GrantedByUserId)
            SELECT tenant.Id, grantRow.MenuId, 1, 1,
                   grantRow.CanAdd, grantRow.CanEdit, grantRow.CanDelete,
                   SYSUTCDATETIME(), N'System: Billing Services menus'
            FROM dbo.Tenants tenant
            CROSS JOIN @MenuGrants grantRow
            WHERE NOT EXISTS (
                SELECT 1 FROM dbo.TenantMenuPermissions existing
                WHERE existing.TenantId = tenant.Id
                  AND existing.MenuId = grantRow.MenuId
            );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            SET NOCOUNT ON;

            DECLARE @ParentId int = (
                SELECT TOP (1) Id FROM dbo.Menus
                WHERE ParentId IS NULL AND Title = N'Billing Services'
                ORDER BY Id
            );

            DECLARE @Targets table (MenuId int NOT NULL);
            INSERT INTO @Targets (MenuId)
            SELECT Id FROM dbo.Menus
            WHERE Route LIKE N'/billing-services/%'
               OR (@ParentId IS NOT NULL AND Id = @ParentId);

            DECLARE @Features table (PermissionId int NOT NULL);
            INSERT INTO @Features (PermissionId)
            SELECT feature.PermissionId
            FROM dbo.Features feature
            JOIN @Targets target
              ON feature.FeatureKey LIKE CONCAT(N'MENU_', target.MenuId, N'%');

            DELETE accessFeature FROM dbo.AccessFeatures accessFeature
            JOIN dbo.StaffMenuAccess staffAccess ON staffAccess.Id = accessFeature.StaffMenuAccessId
            JOIN @Targets target ON target.MenuId = staffAccess.MenuId;
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
