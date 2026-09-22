using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

/// <summary>
/// Adds Annual Reports parent and Annual Exp Report / Annual Income Report /
/// Annual report Filter children, with tenant view grants.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260921180000_AddAnnualReportsMenus")]
public sealed class AddAnnualReportsMenus : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            SET NOCOUNT ON;
            SET ANSI_NULLS ON;
            SET QUOTED_IDENTIFIER ON;

            DECLARE @ParentId int = (
                SELECT TOP (1) Id
                FROM dbo.Menus
                WHERE ParentId IS NULL AND Title = N'Annual Reports'
                ORDER BY Id
            );

            IF @ParentId IS NULL
            BEGIN
                INSERT INTO dbo.Menus (Title, Icon, Route, ParentId, SortOrder, IsActive)
                VALUES (N'Annual Reports', N'CalendarRange', NULL, NULL, 84, 1);
                SET @ParentId = SCOPE_IDENTITY();
            END;
            ELSE
                UPDATE dbo.Menus
                SET Title = N'Annual Reports', Icon = N'CalendarRange', Route = NULL,
                    ParentId = NULL, SortOrder = 84, IsActive = 1
                WHERE Id = @ParentId;

            DECLARE @ExpId int = (
                SELECT TOP (1) Id
                FROM dbo.Menus
                WHERE Route = N'/annual-reports/exp-report'
                   OR (ParentId = @ParentId AND Title = N'Annual Exp Report')
                ORDER BY CASE WHEN Route = N'/annual-reports/exp-report' THEN 0 ELSE 1 END, Id
            );

            IF @ExpId IS NULL
            BEGIN
                INSERT INTO dbo.Menus (Title, Icon, Route, ParentId, SortOrder, IsActive)
                VALUES (N'Annual Exp Report', N'DollarSign', N'/annual-reports/exp-report', @ParentId, 1, 1);
                SET @ExpId = SCOPE_IDENTITY();
            END;
            ELSE
                UPDATE dbo.Menus
                SET Title = N'Annual Exp Report', Icon = N'DollarSign', Route = N'/annual-reports/exp-report',
                    ParentId = @ParentId, SortOrder = 1, IsActive = 1
                WHERE Id = @ExpId;

            DECLARE @IncomeId int = (
                SELECT TOP (1) Id
                FROM dbo.Menus
                WHERE Route = N'/annual-reports/income-report'
                   OR (ParentId = @ParentId AND Title = N'Annual Income Report')
                ORDER BY CASE WHEN Route = N'/annual-reports/income-report' THEN 0 ELSE 1 END, Id
            );

            IF @IncomeId IS NULL
            BEGIN
                INSERT INTO dbo.Menus (Title, Icon, Route, ParentId, SortOrder, IsActive)
                VALUES (N'Annual Income Report', N'LineChart', N'/annual-reports/income-report', @ParentId, 2, 1);
                SET @IncomeId = SCOPE_IDENTITY();
            END;
            ELSE
                UPDATE dbo.Menus
                SET Title = N'Annual Income Report', Icon = N'LineChart', Route = N'/annual-reports/income-report',
                    ParentId = @ParentId, SortOrder = 2, IsActive = 1
                WHERE Id = @IncomeId;

            DECLARE @FilterId int = (
                SELECT TOP (1) Id
                FROM dbo.Menus
                WHERE Route = N'/annual-reports/filter'
                   OR (ParentId = @ParentId AND Title IN (N'Annual report Filter', N'Annual Report Filter'))
                ORDER BY CASE WHEN Route = N'/annual-reports/filter' THEN 0 ELSE 1 END, Id
            );

            IF @FilterId IS NULL
            BEGIN
                INSERT INTO dbo.Menus (Title, Icon, Route, ParentId, SortOrder, IsActive)
                VALUES (N'Annual report Filter', N'SlidersHorizontal', N'/annual-reports/filter', @ParentId, 3, 1);
                SET @FilterId = SCOPE_IDENTITY();
            END;
            ELSE
                UPDATE dbo.Menus
                SET Title = N'Annual report Filter', Icon = N'SlidersHorizontal', Route = N'/annual-reports/filter',
                    ParentId = @ParentId, SortOrder = 3, IsActive = 1
                WHERE Id = @FilterId;

            DECLARE @Targets table (MenuId int NOT NULL, Title nvarchar(100) NOT NULL);
            INSERT INTO @Targets (MenuId, Title)
            VALUES
                (@ExpId, N'Annual Exp Report'),
                (@IncomeId, N'Annual Income Report'),
                (@FilterId, N'Annual report Filter');

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
                   SYSUTCDATETIME(), N'System: Annual Reports menus'
            FROM dbo.Tenants tenant
            CROSS JOIN (
                SELECT @ParentId AS MenuId
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
            WHERE Route IN (
                    N'/annual-reports/exp-report',
                    N'/annual-reports/income-report',
                    N'/annual-reports/filter')
               OR (ParentId IS NULL AND Title = N'Annual Reports');

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
