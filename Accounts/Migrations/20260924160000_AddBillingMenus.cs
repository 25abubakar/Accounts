using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

/// <summary>
/// Adds the Billing parent with Receipt (Roz), Claims, Patients, and
/// Billing (Roz) Excel child menus, including tenant ceilings and actions.
/// Apply script: Sql/Apply/Apply_BillingMenus.sql
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260924160000_AddBillingMenus")]
public sealed class AddBillingMenus : Migration
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
                WHERE ParentId IS NULL AND Title = N'Billing'
                ORDER BY Id
            );

            IF @ParentId IS NULL
            BEGIN
                INSERT INTO dbo.Menus (Title, Icon, Route, ParentId, SortOrder, IsActive)
                VALUES (N'Billing', N'Users', NULL, NULL, 88, 1);
                SET @ParentId = SCOPE_IDENTITY();
            END;
            ELSE
                UPDATE dbo.Menus
                SET Title = N'Billing', Icon = N'Users', Route = NULL,
                    ParentId = NULL, SortOrder = 88, IsActive = 1
                WHERE Id = @ParentId;

            DECLARE @ReceiptId int = (
                SELECT TOP (1) Id FROM dbo.Menus
                WHERE Route = N'/billing/receipt-roz'
                   OR (ParentId = @ParentId AND Title IN (N'Receipt (Roz)', N'Receipt (ROZ)'))
                ORDER BY CASE WHEN Route = N'/billing/receipt-roz' THEN 0 ELSE 1 END, Id
            );
            IF @ReceiptId IS NULL
            BEGIN
                INSERT INTO dbo.Menus (Title, Icon, Route, ParentId, SortOrder, IsActive)
                VALUES (N'Receipt (Roz)', N'ReceiptText', N'/billing/receipt-roz', @ParentId, 1, 1);
                SET @ReceiptId = SCOPE_IDENTITY();
            END;
            ELSE
                UPDATE dbo.Menus SET Title = N'Receipt (Roz)', Icon = N'ReceiptText',
                    Route = N'/billing/receipt-roz', ParentId = @ParentId, SortOrder = 1, IsActive = 1
                WHERE Id = @ReceiptId;

            DECLARE @ClaimsId int = (
                SELECT TOP (1) Id FROM dbo.Menus
                WHERE Route = N'/billing/claims'
                   OR (ParentId = @ParentId AND Title = N'Claims')
                ORDER BY CASE WHEN Route = N'/billing/claims' THEN 0 ELSE 1 END, Id
            );
            IF @ClaimsId IS NULL
            BEGIN
                INSERT INTO dbo.Menus (Title, Icon, Route, ParentId, SortOrder, IsActive)
                VALUES (N'Claims', N'ListChecks', N'/billing/claims', @ParentId, 2, 1);
                SET @ClaimsId = SCOPE_IDENTITY();
            END;
            ELSE
                UPDATE dbo.Menus SET Title = N'Claims', Icon = N'ListChecks',
                    Route = N'/billing/claims', ParentId = @ParentId, SortOrder = 2, IsActive = 1
                WHERE Id = @ClaimsId;

            DECLARE @PatientsId int = (
                SELECT TOP (1) Id FROM dbo.Menus
                WHERE Route = N'/billing/patients'
                   OR (ParentId = @ParentId AND Title = N'Patients')
                ORDER BY CASE WHEN Route = N'/billing/patients' THEN 0 ELSE 1 END, Id
            );
            IF @PatientsId IS NULL
            BEGIN
                INSERT INTO dbo.Menus (Title, Icon, Route, ParentId, SortOrder, IsActive)
                VALUES (N'Patients', N'UserCheck', N'/billing/patients', @ParentId, 3, 1);
                SET @PatientsId = SCOPE_IDENTITY();
            END;
            ELSE
                UPDATE dbo.Menus SET Title = N'Patients', Icon = N'UserCheck',
                    Route = N'/billing/patients', ParentId = @ParentId, SortOrder = 3, IsActive = 1
                WHERE Id = @PatientsId;

            DECLARE @ExcelId int = (
                SELECT TOP (1) Id FROM dbo.Menus
                WHERE Route = N'/billing/roz-excel'
                   OR (ParentId = @ParentId AND Title IN (N'Billing (Roz) Excel', N'Billing (ROZ) Excel'))
                ORDER BY CASE WHEN Route = N'/billing/roz-excel' THEN 0 ELSE 1 END, Id
            );
            IF @ExcelId IS NULL
            BEGIN
                INSERT INTO dbo.Menus (Title, Icon, Route, ParentId, SortOrder, IsActive)
                VALUES (N'Billing (Roz) Excel', N'FileCog', N'/billing/roz-excel', @ParentId, 4, 1);
                SET @ExcelId = SCOPE_IDENTITY();
            END;
            ELSE
                UPDATE dbo.Menus SET Title = N'Billing (Roz) Excel', Icon = N'FileCog',
                    Route = N'/billing/roz-excel', ParentId = @ParentId, SortOrder = 4, IsActive = 1
                WHERE Id = @ExcelId;

            DECLARE @Targets table (MenuId int NOT NULL, Title nvarchar(100) NOT NULL);
            INSERT INTO @Targets (MenuId, Title)
            VALUES
                (@ReceiptId, N'Receipt (Roz)'),
                (@ClaimsId, N'Claims'),
                (@PatientsId, N'Patients'),
                (@ExcelId, N'Billing (Roz) Excel');

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
            SELECT tenant.Id, menuGrant.MenuId, 1, 1,
                   menuGrant.CanAdd, menuGrant.CanEdit, menuGrant.CanDelete,
                   SYSUTCDATETIME(), N'System: Billing menus'
            FROM dbo.Tenants tenant
            CROSS JOIN @MenuGrants menuGrant
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

            DECLARE @ParentId int = (
                SELECT TOP (1) Id FROM dbo.Menus
                WHERE ParentId IS NULL AND Title = N'Billing'
                ORDER BY Id
            );

            DECLARE @Targets table (MenuId int NOT NULL);
            INSERT INTO @Targets (MenuId)
            SELECT Id FROM dbo.Menus
            WHERE Route IN (
                N'/billing/receipt-roz',
                N'/billing/claims',
                N'/billing/patients',
                N'/billing/roz-excel')
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
