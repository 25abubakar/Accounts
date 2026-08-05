using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260804143000_AddAssessmentMenus")]
public sealed class AddAssessmentMenus : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DECLARE @AssessmentId int = (
                SELECT TOP (1) Id FROM Menus
                WHERE ParentId IS NULL AND Title IN (N'Assessment', N'Assesment')
                ORDER BY CASE WHEN Title = N'Assessment' THEN 0 ELSE 1 END, Id
            );

            IF @AssessmentId IS NULL
            BEGIN
                INSERT INTO Menus (Title, Icon, Route, ParentId, SortOrder, IsActive)
                VALUES (N'Assessment', N'ClipboardCheck', NULL, NULL, 5, 1);
                SET @AssessmentId = SCOPE_IDENTITY();
            END
            ELSE
                UPDATE Menus SET Title = N'Assessment', Icon = N'ClipboardCheck', Route = NULL, SortOrder = 5, IsActive = 1
                WHERE Id = @AssessmentId;

            UPDATE Menus SET SortOrder = 6
            WHERE ParentId IS NULL AND Title = N'Platform Settings' AND Id <> @AssessmentId;

            DECLARE @RulesId int = (SELECT TOP (1) Id FROM Menus WHERE Route = N'/assessment/rules');
            IF @RulesId IS NULL
            BEGIN
                INSERT INTO Menus (Title, Icon, Route, ParentId, SortOrder, IsActive)
                VALUES (N'Rules', N'ListChecks', N'/assessment/rules', @AssessmentId, 1, 1);
                SET @RulesId = SCOPE_IDENTITY();
            END
            ELSE
                UPDATE Menus SET Title = N'Rules', Icon = N'ListChecks', ParentId = @AssessmentId, SortOrder = 1, IsActive = 1
                WHERE Id = @RulesId;

            DECLARE @MarkId int = (SELECT TOP (1) Id FROM Menus WHERE Route = N'/assessment/mark');
            IF @MarkId IS NULL
            BEGIN
                INSERT INTO Menus (Title, Icon, Route, ParentId, SortOrder, IsActive)
                VALUES (N'Mark Assessment', N'ClipboardPenLine', N'/assessment/mark', @AssessmentId, 2, 1);
                SET @MarkId = SCOPE_IDENTITY();
            END
            ELSE
                UPDATE Menus SET Title = N'Mark Assessment', Icon = N'ClipboardPenLine', ParentId = @AssessmentId, SortOrder = 2, IsActive = 1
                WHERE Id = @MarkId;

            DECLARE @Seed TABLE (MenuId int, Title nvarchar(100));
            INSERT INTO @Seed VALUES
                (@AssessmentId, N'Assessment'), (@RulesId, N'Rules'), (@MarkId, N'Mark Assessment');

            INSERT INTO Features (FeatureKey, FeatureName, Module, Description, CreatedDate)
            SELECT CONCAT(N'MENU_', seed.MenuId, suffix.Suffix),
                   CONCAT(seed.Title, suffix.DisplayName), N'Menu',
                   CONCAT(suffix.ActionName, N' ', seed.Title), SYSUTCDATETIME()
            FROM @Seed seed
            CROSS JOIN (VALUES
                (N'', N'', N'Open'), (N'_VIEW', N' - View', N'View'),
                (N'_ADD', N' - Add', N'Add'), (N'_EDIT', N' - Edit', N'Edit'),
                (N'_DELETE', N' - Delete', N'Delete')
            ) suffix(Suffix, DisplayName, ActionName)
            WHERE NOT EXISTS (
                SELECT 1 FROM Features existing
                WHERE existing.FeatureKey = CONCAT(N'MENU_', seed.MenuId, suffix.Suffix));

            INSERT INTO MenuPermissions (MenuId, PermissionId)
            SELECT seed.MenuId, feature.PermissionId
            FROM @Seed seed
            JOIN Features feature ON feature.FeatureKey IN (
                CONCAT(N'MENU_', seed.MenuId), CONCAT(N'MENU_', seed.MenuId, N'_VIEW'),
                CONCAT(N'MENU_', seed.MenuId, N'_ADD'), CONCAT(N'MENU_', seed.MenuId, N'_EDIT'),
                CONCAT(N'MENU_', seed.MenuId, N'_DELETE'))
            WHERE NOT EXISTS (
                SELECT 1 FROM MenuPermissions existing
                WHERE existing.MenuId = seed.MenuId AND existing.PermissionId = feature.PermissionId);

            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DECLARE @Ids TABLE (Id int);
            INSERT INTO @Ids SELECT Id FROM Menus
            WHERE Route IN (N'/assessment/rules', N'/assessment/mark')
               OR (ParentId IS NULL AND Title IN (N'Assessment', N'Assesment'));

            DELETE af FROM AccessFeatures af
            JOIN Features f ON f.PermissionId = af.PermissionId
            JOIN @Ids ids ON f.FeatureKey LIKE CONCAT(N'MENU_', ids.Id, N'%');
            DELETE mp FROM MenuPermissions mp JOIN @Ids ids ON ids.Id = mp.MenuId;
            DELETE sma FROM StaffMenuAccess sma JOIN @Ids ids ON ids.Id = sma.MenuId;
            DELETE tmp FROM TenantMenuPermissions tmp JOIN @Ids ids ON ids.Id = tmp.MenuId;
            DELETE f FROM Features f JOIN @Ids ids ON f.FeatureKey LIKE CONCAT(N'MENU_', ids.Id, N'%');
            DELETE m FROM Menus m JOIN @Ids ids ON ids.Id = m.Id;
            UPDATE Menus SET SortOrder = 5 WHERE ParentId IS NULL AND Title = N'Platform Settings';
            """);
    }
}
