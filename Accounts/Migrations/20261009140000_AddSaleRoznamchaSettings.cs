using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261009140000_AddSaleRoznamchaSettings")]
public sealed class AddSaleRoznamchaSettings : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(ExecutableBatch(SaleRoznamchaSql.SettingsListProcedure));
        migrationBuilder.Sql(ExecutableBatch(SaleRoznamchaSql.SettingsSaveProcedure));
        migrationBuilder.Sql(ExecutableBatch(SaleRoznamchaSql.SettingsDeactivateProcedure));

        migrationBuilder.Sql(
            """
            SET NOCOUNT ON;
            SET XACT_ABORT ON;

            DECLARE @ParentId int = (
                SELECT TOP (1) Id FROM dbo.Menus
                WHERE ParentId IS NULL AND Title = N'Sale Roznamcha'
                ORDER BY Id
            );

            IF @ParentId IS NULL
            BEGIN
                INSERT dbo.Menus (Title, Icon, Route, ParentId, SortOrder, IsActive)
                VALUES (N'Sale Roznamcha', N'ShoppingBag', NULL, NULL, 91, 1);
                SET @ParentId = SCOPE_IDENTITY();
            END;

            DECLARE @MenuId int = (
                SELECT TOP (1) Id FROM dbo.Menus
                WHERE Route = N'/sale-roznamcha/settings'
                   OR (ParentId = @ParentId AND Title = N'Settings')
                ORDER BY CASE WHEN Route = N'/sale-roznamcha/settings' THEN 0 ELSE 1 END, Id
            );

            IF @MenuId IS NULL
            BEGIN
                INSERT dbo.Menus (Title, Icon, Route, ParentId, SortOrder, IsActive)
                VALUES (N'Settings', N'Settings', N'/sale-roznamcha/settings', @ParentId, 5, 1);
                SET @MenuId = SCOPE_IDENTITY();
            END
            ELSE
                UPDATE dbo.Menus SET Title = N'Settings', Icon = N'Settings',
                    Route = N'/sale-roznamcha/settings', ParentId = @ParentId,
                    SortOrder = 5, IsActive = 1
                WHERE Id = @MenuId;

            DECLARE @Features table
            (
                FeatureKey nvarchar(150) NOT NULL,
                FeatureName nvarchar(150) NOT NULL,
                Description nvarchar(500) NOT NULL
            );

            INSERT @Features (FeatureKey, FeatureName, Description)
            VALUES
                (CONCAT(N'MENU_', @MenuId), N'Settings', N'Open the Sale Roznamcha Settings screen.'),
                (CONCAT(N'MENU_', @MenuId, N'_VIEW'), N'Settings View', N'View Sale Roznamcha settings.'),
                (CONCAT(N'MENU_', @MenuId, N'_ADD'), N'Settings Add', N'Add Sale Roznamcha settings.'),
                (CONCAT(N'MENU_', @MenuId, N'_EDIT'), N'Settings Edit', N'Edit Sale Roznamcha settings.'),
                (CONCAT(N'MENU_', @MenuId, N'_DELETE'), N'Settings Delete', N'Deactivate Sale Roznamcha settings.');

            INSERT dbo.Features (FeatureKey, FeatureName, Module, Description, CreatedDate)
            SELECT source.FeatureKey, source.FeatureName, N'Menu', source.Description, SYSUTCDATETIME()
            FROM @Features source
            WHERE NOT EXISTS (SELECT 1 FROM dbo.Features existing WHERE existing.FeatureKey = source.FeatureKey);

            INSERT dbo.MenuPermissions (MenuId, PermissionId)
            SELECT @MenuId, feature.PermissionId
            FROM dbo.Features feature
            JOIN @Features source ON source.FeatureKey = feature.FeatureKey
            WHERE NOT EXISTS (
                SELECT 1 FROM dbo.MenuPermissions existing
                WHERE existing.MenuId = @MenuId AND existing.PermissionId = feature.PermissionId
            );

            INSERT dbo.TenantMenuPermissions
                (TenantId, MenuId, CanView, CanAdd, CanEdit, CanDelete, IsAllow, GrantedOnUtc)
            SELECT tenant.Id, @MenuId, 1, 1, 1, 1, 1, SYSUTCDATETIME()
            FROM dbo.Tenants tenant
            WHERE NOT EXISTS (
                SELECT 1 FROM dbo.TenantMenuPermissions existing
                WHERE existing.TenantId = tenant.Id AND existing.MenuId = @MenuId
            );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.usp_SaleRoznamcha_SettingDeactivate;");
        migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.usp_SaleRoznamcha_SettingSave;");
        migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.usp_SaleRoznamcha_SettingList;");
        migrationBuilder.Sql(
            """
            DECLARE @MenuId int = (SELECT TOP (1) Id FROM dbo.Menus WHERE Route = N'/sale-roznamcha/settings');
            IF @MenuId IS NOT NULL
            BEGIN
                DELETE FROM dbo.MenuPermissions WHERE MenuId = @MenuId;
                DELETE FROM dbo.TenantMenuPermissions WHERE MenuId = @MenuId;
                DELETE FROM dbo.Menus WHERE Id = @MenuId;
            END;
            """);
    }

    private static string ExecutableBatch(string sql) => $"EXEC(N'{sql.Replace("'", "''")}');";
}
