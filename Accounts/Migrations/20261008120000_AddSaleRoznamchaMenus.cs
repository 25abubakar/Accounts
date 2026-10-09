using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

/// <summary>
/// Adds the tenant-visible Sale Roznamcha navigation structure and its
/// route-level permission features. Business tables are intentionally not
/// created here because their fields and rules have not yet been specified.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20261008120000_AddSaleRoznamchaMenus")]
public sealed class AddSaleRoznamchaMenus : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            SET NOCOUNT ON;
            SET XACT_ABORT ON;

            DECLARE @ParentId int = (
                SELECT TOP (1) Id
                FROM dbo.Menus
                WHERE ParentId IS NULL
                  AND Title IN (N'Sale Roznamcha', N'Sales Roznamcha')
                ORDER BY CASE WHEN Title = N'Sale Roznamcha' THEN 0 ELSE 1 END, Id
            );

            IF @ParentId IS NULL
            BEGIN
                INSERT INTO dbo.Menus (Title, Icon, Route, ParentId, SortOrder, IsActive)
                VALUES (N'Sale Roznamcha', N'ShoppingBag', NULL, NULL, 91, 1);
                SET @ParentId = SCOPE_IDENTITY();
            END
            ELSE
                UPDATE dbo.Menus
                SET Title = N'Sale Roznamcha', Icon = N'ShoppingBag', Route = NULL,
                    ParentId = NULL, SortOrder = 91, IsActive = 1
                WHERE Id = @ParentId;

            DECLARE @Children table
            (
                Title nvarchar(100) NOT NULL,
                Icon nvarchar(100) NOT NULL,
                Route nvarchar(250) NOT NULL,
                SortOrder int NOT NULL
            );

            INSERT INTO @Children (Title, Icon, Route, SortOrder)
            VALUES
                (N'Management', N'SlidersHorizontal', N'/sale-roznamcha/management', 1),
                (N'Inventory',  N'Package',           N'/sale-roznamcha/inventory',  2),
                (N'Daily Sale', N'ShoppingBag',       N'/sale-roznamcha/daily-sale', 3),
                (N'History',    N'Clock3',            N'/sale-roznamcha/history',    4);

            DECLARE @MenuIds table (MenuId int NOT NULL, Title nvarchar(100) NOT NULL);

            DECLARE @Title nvarchar(100), @Icon nvarchar(100), @Route nvarchar(250), @SortOrder int, @MenuId int;
            DECLARE child_cursor CURSOR LOCAL FAST_FORWARD FOR
                SELECT Title, Icon, Route, SortOrder FROM @Children ORDER BY SortOrder;

            OPEN child_cursor;
            FETCH NEXT FROM child_cursor INTO @Title, @Icon, @Route, @SortOrder;
            WHILE @@FETCH_STATUS = 0
            BEGIN
                SET @MenuId = (
                    SELECT TOP (1) Id
                    FROM dbo.Menus
                    WHERE Route = @Route OR (ParentId = @ParentId AND Title = @Title)
                    ORDER BY CASE WHEN Route = @Route THEN 0 ELSE 1 END, Id
                );

                IF @MenuId IS NULL
                BEGIN
                    INSERT INTO dbo.Menus (Title, Icon, Route, ParentId, SortOrder, IsActive)
                    VALUES (@Title, @Icon, @Route, @ParentId, @SortOrder, 1);
                    SET @MenuId = SCOPE_IDENTITY();
                END
                ELSE
                    UPDATE dbo.Menus
                    SET Title = @Title, Icon = @Icon, Route = @Route,
                        ParentId = @ParentId, SortOrder = @SortOrder, IsActive = 1
                    WHERE Id = @MenuId;

                INSERT INTO @MenuIds (MenuId, Title) VALUES (@MenuId, @Title);
                FETCH NEXT FROM child_cursor INTO @Title, @Icon, @Route, @SortOrder;
            END;
            CLOSE child_cursor;
            DEALLOCATE child_cursor;

            INSERT INTO dbo.Features (FeatureKey, FeatureName, Module, Description, CreatedDate)
            SELECT CONCAT(N'MENU_', item.MenuId), item.Title, N'Menu',
                   CONCAT(N'Open the ', item.Title, N' screen.'), SYSUTCDATETIME()
            FROM @MenuIds item
            WHERE NOT EXISTS (
                SELECT 1 FROM dbo.Features feature
                WHERE feature.FeatureKey = CONCAT(N'MENU_', item.MenuId)
            );

            INSERT INTO dbo.Features (FeatureKey, FeatureName, Module, Description, CreatedDate)
            SELECT source.FeatureKey, source.FeatureName, N'Menu', source.Description, SYSUTCDATETIME()
            FROM (
                SELECT CONCAT(N'MENU_', item.MenuId, N'_VIEW') FeatureKey, CONCAT(item.Title, N' View') FeatureName, CONCAT(N'View ', item.Title, N'.') Description FROM @MenuIds item
                UNION ALL SELECT CONCAT(N'MENU_', item.MenuId, N'_ADD'), CONCAT(item.Title, N' Add'), CONCAT(N'Add ', item.Title, N'.') FROM @MenuIds item
                UNION ALL SELECT CONCAT(N'MENU_', item.MenuId, N'_EDIT'), CONCAT(item.Title, N' Edit'), CONCAT(N'Edit ', item.Title, N'.') FROM @MenuIds item
                UNION ALL SELECT CONCAT(N'MENU_', item.MenuId, N'_DELETE'), CONCAT(item.Title, N' Delete'), CONCAT(N'Delete ', item.Title, N'.') FROM @MenuIds item
            ) source
            WHERE NOT EXISTS (
                SELECT 1 FROM dbo.Features feature WHERE feature.FeatureKey = source.FeatureKey
            );

            INSERT INTO dbo.MenuPermissions (MenuId, PermissionId)
            SELECT item.MenuId, feature.PermissionId
            FROM @MenuIds item
            JOIN dbo.Features feature ON feature.FeatureKey IN (
                CONCAT(N'MENU_', item.MenuId),
                CONCAT(N'MENU_', item.MenuId, N'_VIEW'),
                CONCAT(N'MENU_', item.MenuId, N'_ADD'),
                CONCAT(N'MENU_', item.MenuId, N'_EDIT'),
                CONCAT(N'MENU_', item.MenuId, N'_DELETE')
            )
            WHERE NOT EXISTS (
                SELECT 1 FROM dbo.MenuPermissions permission
                WHERE permission.MenuId = item.MenuId
                  AND permission.PermissionId = feature.PermissionId
            );

            INSERT INTO dbo.TenantMenuPermissions
                (TenantId, MenuId, CanView, CanAdd, CanEdit, CanDelete, IsAllow, GrantedOnUtc)
            SELECT tenant.Id, item.MenuId, 1, 1, 1, 1, 1, SYSUTCDATETIME()
            FROM dbo.Tenants tenant
            CROSS JOIN @MenuIds item
            WHERE NOT EXISTS (
                SELECT 1 FROM dbo.TenantMenuPermissions permission
                WHERE permission.TenantId = tenant.Id AND permission.MenuId = item.MenuId
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
                WHERE ParentId IS NULL AND Title = N'Sale Roznamcha'
                ORDER BY Id
            );

            DECLARE @ChildIds table (Id int NOT NULL);
            INSERT INTO @ChildIds (Id)
            SELECT Id FROM dbo.Menus
            WHERE Route IN (
                N'/sale-roznamcha/management',
                N'/sale-roznamcha/inventory',
                N'/sale-roznamcha/daily-sale',
                N'/sale-roznamcha/history'
            );

            DELETE permission FROM dbo.MenuPermissions permission
            JOIN @ChildIds child ON child.Id = permission.MenuId;

            DELETE permission FROM dbo.TenantMenuPermissions permission
            JOIN @ChildIds child ON child.Id = permission.MenuId;

            DELETE FROM dbo.Menus WHERE Id IN (SELECT Id FROM @ChildIds);
            IF @ParentId IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM dbo.Menus WHERE ParentId = @ParentId)
                DELETE FROM dbo.Menus WHERE Id = @ParentId;
            """);
    }
}
