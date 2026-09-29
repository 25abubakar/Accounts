using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

/// <summary>
/// E-Marketing parent + Stock Info / Sales (Roz) child menus.
/// Apply script: Sql/Apply/Apply_EMarketingMenus.sql
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260924120000_AddEMarketingModule")]
public sealed class AddEMarketingModule : Migration
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
                WHERE ParentId IS NULL
                  AND Title IN (N'E-Marketing', N'E_Marketing', N'E Marketing')
                ORDER BY CASE Title
                    WHEN N'E-Marketing' THEN 0
                    WHEN N'E_Marketing' THEN 1
                    ELSE 2
                END, Id
            );

            IF @ParentId IS NULL
            BEGIN
                INSERT INTO dbo.Menus (Title, Icon, Route, ParentId, SortOrder, IsActive)
                VALUES (N'E-Marketing', N'Users', NULL, NULL, 87, 1);
                SET @ParentId = SCOPE_IDENTITY();
            END;
            ELSE
                UPDATE dbo.Menus
                SET Title = N'E-Marketing', Icon = N'Users', Route = NULL,
                    ParentId = NULL, SortOrder = 87, IsActive = 1
                WHERE Id = @ParentId;

            DECLARE @StockId int = (
                SELECT TOP (1) Id
                FROM dbo.Menus
                WHERE Route = N'/e-marketing/stock-info'
                   OR (ParentId = @ParentId AND Title IN (N'Stock Info', N'StockInfo'))
                ORDER BY CASE WHEN Route = N'/e-marketing/stock-info' THEN 0 ELSE 1 END, Id
            );

            IF @StockId IS NULL
            BEGIN
                INSERT INTO dbo.Menus (Title, Icon, Route, ParentId, SortOrder, IsActive)
                VALUES (N'Stock Info', N'Package', N'/e-marketing/stock-info', @ParentId, 1, 1);
                SET @StockId = SCOPE_IDENTITY();
            END;
            ELSE
                UPDATE dbo.Menus
                SET Title = N'Stock Info', Icon = N'Package', Route = N'/e-marketing/stock-info',
                    ParentId = @ParentId, SortOrder = 1, IsActive = 1
                WHERE Id = @StockId;

            DECLARE @SalesId int = (
                SELECT TOP (1) Id
                FROM dbo.Menus
                WHERE Route = N'/e-marketing/sales-roz'
                   OR (ParentId = @ParentId AND Title IN (N'Sales (Roz)', N'Sales Roz', N'Sales'))
                ORDER BY CASE WHEN Route = N'/e-marketing/sales-roz' THEN 0 ELSE 1 END, Id
            );

            IF @SalesId IS NULL
            BEGIN
                INSERT INTO dbo.Menus (Title, Icon, Route, ParentId, SortOrder, IsActive)
                VALUES (N'Sales (Roz)', N'ShoppingBag', N'/e-marketing/sales-roz', @ParentId, 2, 1);
                SET @SalesId = SCOPE_IDENTITY();
            END;
            ELSE
                UPDATE dbo.Menus
                SET Title = N'Sales (Roz)', Icon = N'ShoppingBag', Route = N'/e-marketing/sales-roz',
                    ParentId = @ParentId, SortOrder = 2, IsActive = 1
                WHERE Id = @SalesId;

            DECLARE @Targets table (MenuId int NOT NULL, Title nvarchar(100) NOT NULL);
            INSERT INTO @Targets (MenuId, Title)
            VALUES
                (@StockId, N'Stock Info'),
                (@SalesId, N'Sales (Roz)');

            INSERT INTO dbo.Features (FeatureKey, FeatureName, Module, Description, CreatedDate)
            SELECT CONCAT(N'MENU_', target.MenuId), target.Title, N'Menu',
                   CONCAT(N'Open the ', target.Title, N' screen.'), SYSUTCDATETIME()
            FROM @Targets target
            WHERE NOT EXISTS (
                SELECT 1 FROM dbo.Features feature
                WHERE feature.FeatureKey = CONCAT(N'MENU_', target.MenuId)
            );

            INSERT INTO dbo.Features (FeatureKey, FeatureName, Module, Description, CreatedDate)
            SELECT feature.FeatureKey, feature.FeatureName, N'Menu', feature.Description, SYSUTCDATETIME()
            FROM (
                SELECT CONCAT(N'MENU_', t.MenuId, N'_VIEW') AS FeatureKey, CONCAT(t.Title, N' View') AS FeatureName, CONCAT(N'View ', t.Title, N'.') AS Description FROM @Targets t
                UNION ALL SELECT CONCAT(N'MENU_', t.MenuId, N'_ADD'), CONCAT(t.Title, N' Add'), CONCAT(N'Add ', t.Title, N'.') FROM @Targets t
                UNION ALL SELECT CONCAT(N'MENU_', t.MenuId, N'_EDIT'), CONCAT(t.Title, N' Edit'), CONCAT(N'Edit ', t.Title, N'.') FROM @Targets t
                UNION ALL SELECT CONCAT(N'MENU_', t.MenuId, N'_DELETE'), CONCAT(t.Title, N' Delete'), CONCAT(N'Delete ', t.Title, N'.') FROM @Targets t
            ) feature
            WHERE NOT EXISTS (SELECT 1 FROM dbo.Features f WHERE f.FeatureKey = feature.FeatureKey);

            INSERT INTO dbo.MenuPermissions (MenuId, PermissionId)
            SELECT t.MenuId, f.PermissionId
            FROM @Targets t
            INNER JOIN dbo.Features f ON f.FeatureKey IN (
                CONCAT(N'MENU_', t.MenuId),
                CONCAT(N'MENU_', t.MenuId, N'_VIEW'),
                CONCAT(N'MENU_', t.MenuId, N'_ADD'),
                CONCAT(N'MENU_', t.MenuId, N'_EDIT'),
                CONCAT(N'MENU_', t.MenuId, N'_DELETE')
            )
            WHERE NOT EXISTS (
                SELECT 1 FROM dbo.MenuPermissions mp
                WHERE mp.MenuId = t.MenuId AND mp.PermissionId = f.PermissionId
            );

            INSERT INTO dbo.TenantMenuPermissions (TenantId, MenuId, CanView, CanAdd, CanEdit, CanDelete, IsAllow, GrantedOnUtc)
            SELECT ten.Id, t.MenuId, 1, 1, 1, 1, 1, SYSUTCDATETIME()
            FROM dbo.Tenants ten
            CROSS JOIN @Targets t
            WHERE NOT EXISTS (
                SELECT 1 FROM dbo.TenantMenuPermissions tmp
                WHERE tmp.TenantId = ten.Id AND tmp.MenuId = t.MenuId
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
                WHERE ParentId IS NULL AND Title = N'E-Marketing'
                ORDER BY Id
            );

            DECLARE @ChildIds table (Id int NOT NULL);
            INSERT INTO @ChildIds (Id)
            SELECT Id FROM dbo.Menus
            WHERE Route IN (N'/e-marketing/stock-info', N'/e-marketing/sales-roz')
               OR (@ParentId IS NOT NULL AND ParentId = @ParentId);

            DELETE mp FROM dbo.MenuPermissions mp
            INNER JOIN @ChildIds c ON c.Id = mp.MenuId;

            DELETE tmp FROM dbo.TenantMenuPermissions tmp
            INNER JOIN @ChildIds c ON c.Id = tmp.MenuId;

            DELETE f FROM dbo.Features f
            INNER JOIN @ChildIds c ON f.FeatureKey LIKE CONCAT(N'MENU_', c.Id, N'%');

            DELETE FROM dbo.Menus WHERE Id IN (SELECT Id FROM @ChildIds);
            IF @ParentId IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM dbo.Menus WHERE ParentId = @ParentId)
                DELETE FROM dbo.Menus WHERE Id = @ParentId;
            """);
    }
}
