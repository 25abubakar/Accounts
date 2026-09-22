using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

/// <summary>
/// Adds Library -> NFC and Library -> NFC List and carries forward the
/// existing Library screen's tenant, role, and person access grants.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260921120000_AddLibraryNfcMenus")]
public sealed class AddLibraryNfcMenus : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            SET NOCOUNT ON;

            DECLARE @LibraryParentId int = (
                SELECT TOP (1) Id
                FROM dbo.Menus
                WHERE ParentId IS NULL AND Title = N'Library'
                ORDER BY Id
            );

            IF @LibraryParentId IS NULL
            BEGIN
                INSERT INTO dbo.Menus (Title, Icon, Route, ParentId, SortOrder, IsActive)
                VALUES (N'Library', N'LibraryBig', NULL, NULL, 80, 1);
                SET @LibraryParentId = SCOPE_IDENTITY();
            END;

            DECLARE @SourceMenuId int = (
                SELECT TOP (1) Id
                FROM dbo.Menus
                WHERE Route = N'/library'
                ORDER BY Id
            );

            DECLARE @NfcId int = (
                SELECT TOP (1) Id
                FROM dbo.Menus
                WHERE Route = N'/library/nfc'
                   OR (ParentId = @LibraryParentId AND Title = N'NFC')
                ORDER BY CASE WHEN Route = N'/library/nfc' THEN 0 ELSE 1 END, Id
            );

            IF @NfcId IS NULL
            BEGIN
                INSERT INTO dbo.Menus (Title, Icon, Route, ParentId, SortOrder, IsActive)
                VALUES (N'NFC', N'Nfc', N'/library/nfc', @LibraryParentId, 5, 1);
                SET @NfcId = SCOPE_IDENTITY();
            END;
            ELSE
                UPDATE dbo.Menus
                SET Title = N'NFC', Icon = N'Nfc', Route = N'/library/nfc',
                    ParentId = @LibraryParentId, SortOrder = 5, IsActive = 1
                WHERE Id = @NfcId;

            DECLARE @NfcListId int = (
                SELECT TOP (1) Id
                FROM dbo.Menus
                WHERE Route = N'/library/nfc-list'
                   OR (ParentId = @LibraryParentId AND Title = N'NFC List')
                ORDER BY CASE WHEN Route = N'/library/nfc-list' THEN 0 ELSE 1 END, Id
            );

            IF @NfcListId IS NULL
            BEGIN
                INSERT INTO dbo.Menus (Title, Icon, Route, ParentId, SortOrder, IsActive)
                VALUES (N'NFC List', N'List', N'/library/nfc-list', @LibraryParentId, 6, 1);
                SET @NfcListId = SCOPE_IDENTITY();
            END;
            ELSE
                UPDATE dbo.Menus
                SET Title = N'NFC List', Icon = N'List', Route = N'/library/nfc-list',
                    ParentId = @LibraryParentId, SortOrder = 6, IsActive = 1
                WHERE Id = @NfcListId;

            DECLARE @Targets table (MenuId int NOT NULL, Title nvarchar(100) NOT NULL);
            INSERT INTO @Targets (MenuId, Title)
            VALUES (@NfcId, N'NFC'), (@NfcListId, N'NFC List');

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

            -- Preserve the tenant ceiling: only tenants that can open Library
            -- receive the new children, with the same view allowance.
            IF @SourceMenuId IS NOT NULL
            BEGIN
                INSERT INTO dbo.TenantMenuPermissions
                    (TenantId, MenuId, IsAllow, CanView, CanAdd, CanEdit, CanDelete, GrantedOnUtc, GrantedByUserId)
                SELECT source.TenantId, target.MenuId, source.IsAllow, source.CanView,
                       0, 0, 0, SYSUTCDATETIME(), N'System: Library NFC menus'
                FROM dbo.TenantMenuPermissions source
                CROSS JOIN @Targets target
                WHERE source.MenuId = @SourceMenuId
                  AND NOT EXISTS (
                      SELECT 1 FROM dbo.TenantMenuPermissions existing
                      WHERE existing.TenantId = source.TenantId
                        AND existing.MenuId = target.MenuId
                  );

                -- Copy person-specific Library grants without replacing any
                -- other menu access already held by the staff member.
                INSERT INTO dbo.StaffMenuAccess (StaffId, MenuId, IsAllow, GrantedBy, GrantedDate)
                SELECT source.StaffId, target.MenuId, source.IsAllow,
                       N'System: Library NFC menus', SYSUTCDATETIME()
                FROM dbo.StaffMenuAccess source
                CROSS JOIN @Targets target
                WHERE source.MenuId = @SourceMenuId
                  AND NOT EXISTS (
                      SELECT 1 FROM dbo.StaffMenuAccess existing
                      WHERE existing.StaffId = source.StaffId
                        AND existing.MenuId = target.MenuId
                  );

                INSERT INTO dbo.AccessFeatures (StaffMenuAccessId, PermissionId, IsAllow)
                SELECT access.Id, feature.PermissionId, 1
                FROM dbo.StaffMenuAccess access
                JOIN @Targets target ON target.MenuId = access.MenuId
                JOIN dbo.Features feature
                  ON feature.FeatureKey IN (
                        CONCAT(N'MENU_', target.MenuId),
                        CONCAT(N'MENU_', target.MenuId, N'_VIEW'))
                WHERE access.IsAllow = 1
                  AND NOT EXISTS (
                      SELECT 1 FROM dbo.AccessFeatures existing
                      WHERE existing.StaffMenuAccessId = access.Id
                        AND existing.PermissionId = feature.PermissionId
                  );

                -- Copy tenant job-title defaults from equivalent Library menu
                -- permissions to each new menu's own feature keys.
                INSERT INTO dbo.TenantRolePermissions
                    (TenantId, JobTitle, DeptId, PermissionId, IsAllowed, CreatedOnUtc, SetByUserId)
                SELECT sourceRole.TenantId, sourceRole.JobTitle, sourceRole.DeptId,
                       targetFeature.PermissionId, sourceRole.IsAllowed,
                       SYSUTCDATETIME(), N'System: Library NFC menus'
                FROM dbo.TenantRolePermissions sourceRole
                JOIN dbo.Features sourceFeature ON sourceFeature.PermissionId = sourceRole.PermissionId
                CROSS JOIN @Targets target
                JOIN dbo.Features targetFeature
                  ON targetFeature.FeatureKey =
                     CASE
                         WHEN sourceFeature.FeatureKey = CONCAT(N'MENU_', @SourceMenuId, N'_VIEW')
                             THEN CONCAT(N'MENU_', target.MenuId, N'_VIEW')
                         ELSE CONCAT(N'MENU_', target.MenuId)
                     END
                WHERE sourceFeature.FeatureKey IN (
                        CONCAT(N'MENU_', @SourceMenuId),
                        CONCAT(N'MENU_', @SourceMenuId, N'_VIEW'))
                  AND NOT EXISTS (
                      SELECT 1 FROM dbo.TenantRolePermissions existing
                      WHERE existing.TenantId = sourceRole.TenantId
                        AND existing.JobTitle = sourceRole.JobTitle
                        AND existing.PermissionId = targetFeature.PermissionId
                        AND ((existing.DeptId IS NULL AND sourceRole.DeptId IS NULL)
                             OR existing.DeptId = sourceRole.DeptId)
                  );

                -- Keep legacy role and department grants working for tenants
                -- that have not yet moved to tenant role defaults.
                INSERT INTO dbo.RolePermissions
                    (JobTitle, DeptId, PermissionId, IsAllowed, CreatedDate)
                SELECT sourceRole.JobTitle, sourceRole.DeptId, targetFeature.PermissionId,
                       sourceRole.IsAllowed, SYSUTCDATETIME()
                FROM dbo.RolePermissions sourceRole
                JOIN dbo.Features sourceFeature ON sourceFeature.PermissionId = sourceRole.PermissionId
                CROSS JOIN @Targets target
                JOIN dbo.Features targetFeature
                  ON targetFeature.FeatureKey =
                     CASE
                         WHEN sourceFeature.FeatureKey = CONCAT(N'MENU_', @SourceMenuId, N'_VIEW')
                             THEN CONCAT(N'MENU_', target.MenuId, N'_VIEW')
                         ELSE CONCAT(N'MENU_', target.MenuId)
                     END
                WHERE sourceFeature.FeatureKey IN (
                        CONCAT(N'MENU_', @SourceMenuId),
                        CONCAT(N'MENU_', @SourceMenuId, N'_VIEW'))
                  AND NOT EXISTS (
                      SELECT 1 FROM dbo.RolePermissions existing
                      WHERE existing.JobTitle = sourceRole.JobTitle
                        AND existing.PermissionId = targetFeature.PermissionId
                        AND ((existing.DeptId IS NULL AND sourceRole.DeptId IS NULL)
                             OR existing.DeptId = sourceRole.DeptId)
                  );

                INSERT INTO dbo.DepartmentAccessMatrix
                    (StaffId, DeptId, PermissionId, HasAccess, GrantedBy, GrantedDate)
                SELECT sourceMatrix.StaffId, sourceMatrix.DeptId, targetFeature.PermissionId,
                       sourceMatrix.HasAccess, N'System: Library NFC menus', SYSUTCDATETIME()
                FROM dbo.DepartmentAccessMatrix sourceMatrix
                JOIN dbo.Features sourceFeature ON sourceFeature.PermissionId = sourceMatrix.PermissionId
                CROSS JOIN @Targets target
                JOIN dbo.Features targetFeature
                  ON targetFeature.FeatureKey =
                     CASE
                         WHEN sourceFeature.FeatureKey = CONCAT(N'MENU_', @SourceMenuId, N'_VIEW')
                             THEN CONCAT(N'MENU_', target.MenuId, N'_VIEW')
                         ELSE CONCAT(N'MENU_', target.MenuId)
                     END
                WHERE sourceFeature.FeatureKey IN (
                        CONCAT(N'MENU_', @SourceMenuId),
                        CONCAT(N'MENU_', @SourceMenuId, N'_VIEW'))
                  AND NOT EXISTS (
                      SELECT 1 FROM dbo.DepartmentAccessMatrix existing
                      WHERE existing.StaffId = sourceMatrix.StaffId
                        AND existing.DeptId = sourceMatrix.DeptId
                        AND existing.PermissionId = targetFeature.PermissionId
                  );
            END;
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
            WHERE Route IN (N'/library/nfc', N'/library/nfc-list');

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
            DELETE menuRow FROM dbo.Menus menuRow
            JOIN @Targets target ON target.MenuId = menuRow.Id;

            DELETE featureRow FROM dbo.Features featureRow
            JOIN @Features feature ON feature.PermissionId = featureRow.PermissionId;
            """);
    }
}
