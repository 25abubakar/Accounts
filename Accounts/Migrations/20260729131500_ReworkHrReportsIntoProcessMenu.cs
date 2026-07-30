using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    [Migration("20260729131500_ReworkHrReportsIntoProcessMenu")]
    public partial class ReworkHrReportsIntoProcessMenu : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DECLARE @HrId int;
SELECT TOP (1) @HrId = Id
FROM dbo.Menus
WHERE Title = N'HR Management' AND ParentId IS NULL
ORDER BY SortOrder, Id;

IF @HrId IS NOT NULL
BEGIN
    DECLARE @LegacyReportsId int;
    DECLARE @ProcessId int;
    DECLARE @ReportId int;
    DECLARE @TaskListId int;

    SELECT TOP (1) @LegacyReportsId = Id
    FROM dbo.Menus
    WHERE ParentId = @HrId
      AND (Route = N'/hr/reports' OR Title = N'Reports')
    ORDER BY CASE WHEN Route = N'/hr/reports' THEN 0 ELSE 1 END, Id;

    SELECT TOP (1) @ProcessId = Id
    FROM dbo.Menus
    WHERE ParentId = @HrId AND Title = N'Process'
    ORDER BY Id;

    IF @ProcessId IS NULL
    BEGIN
        IF @LegacyReportsId IS NOT NULL
        BEGIN
            UPDATE dbo.Menus
            SET Title = N'Process',
                Icon = N'Workflow',
                Route = NULL,
                ParentId = @HrId,
                SortOrder = 4,
                IsActive = 1
            WHERE Id = @LegacyReportsId;

            SET @ProcessId = @LegacyReportsId;
        END
        ELSE
        BEGIN
            INSERT INTO dbo.Menus (Title, Icon, Route, ParentId, SortOrder, IsActive)
            VALUES (N'Process', N'Workflow', NULL, @HrId, 4, 1);

            SET @ProcessId = CONVERT(int, SCOPE_IDENTITY());
        END
    END
    ELSE
    BEGIN
        UPDATE dbo.Menus
        SET Icon = N'Workflow',
            Route = NULL,
            SortOrder = 4,
            IsActive = 1
        WHERE Id = @ProcessId;

        IF @LegacyReportsId IS NOT NULL AND @LegacyReportsId <> @ProcessId
        BEGIN
            DELETE FROM dbo.MenuPermissions WHERE MenuId = @LegacyReportsId;
            DELETE featureAccess
            FROM dbo.AccessFeatures featureAccess
            INNER JOIN dbo.StaffMenuAccess staffAccess
                ON staffAccess.Id = featureAccess.StaffMenuAccessId
            WHERE staffAccess.MenuId = @LegacyReportsId;
            DELETE FROM dbo.StaffMenuAccess WHERE MenuId = @LegacyReportsId;
            DELETE FROM dbo.TenantMenuPermissions WHERE MenuId = @LegacyReportsId;
            DELETE FROM dbo.Menus WHERE Id = @LegacyReportsId;
        END
    END

    SELECT TOP (1) @ReportId = Id
    FROM dbo.Menus
    WHERE Route = N'/hr/process/report'
       OR (ParentId = @ProcessId AND Title IN (N'Report', N'Reports'))
    ORDER BY CASE WHEN Route = N'/hr/process/report' THEN 0 ELSE 1 END, Id;

    IF @ReportId IS NULL
    BEGIN
        INSERT INTO dbo.Menus (Title, Icon, Route, ParentId, SortOrder, IsActive)
        VALUES (N'Reports', N'BarChart2', N'/hr/process/report', @ProcessId, 1, 1);

        SET @ReportId = CONVERT(int, SCOPE_IDENTITY());
    END
    ELSE
    BEGIN
        UPDATE dbo.Menus
        SET Title = N'Reports',
            Icon = N'BarChart2',
            Route = N'/hr/process/report',
            ParentId = @ProcessId,
            SortOrder = 1,
            IsActive = 1
        WHERE Id = @ReportId;
    END

    SELECT TOP (1) @TaskListId = Id
    FROM dbo.Menus
    WHERE Route = N'/hr/process/task-list'
       OR (ParentId = @ProcessId AND Title = N'Task List')
    ORDER BY CASE WHEN Route = N'/hr/process/task-list' THEN 0 ELSE 1 END, Id;

    IF @TaskListId IS NULL
    BEGIN
        INSERT INTO dbo.Menus (Title, Icon, Route, ParentId, SortOrder, IsActive)
        VALUES (N'Task List', N'ListTodo', N'/hr/process/task-list', @ProcessId, 2, 1);

        SET @TaskListId = CONVERT(int, SCOPE_IDENTITY());
    END
    ELSE
    BEGIN
        UPDATE dbo.Menus
        SET Title = N'Task List',
            Icon = N'ListTodo',
            Route = N'/hr/process/task-list',
            ParentId = @ProcessId,
            SortOrder = 2,
            IsActive = 1
        WHERE Id = @TaskListId;
    END

    DECLARE @MenuFeatureSeeds TABLE
    (
        MenuId int NOT NULL,
        Title nvarchar(160) NOT NULL,
        Suffix nvarchar(20) NOT NULL,
        ActionName nvarchar(20) NOT NULL
    );

    INSERT INTO @MenuFeatureSeeds (MenuId, Title, Suffix, ActionName)
    SELECT v.MenuId, v.Title, s.Suffix, s.ActionName
    FROM (VALUES
        (@ProcessId, N'Process'),
        (@ReportId, N'Reports'),
        (@TaskListId, N'Task List')
    ) v(MenuId, Title)
    CROSS JOIN (VALUES
        (N'', N'ACCESS'),
        (N'_VIEW', N'VIEW'),
        (N'_ADD', N'ADD'),
        (N'_EDIT', N'EDIT'),
        (N'_DELETE', N'DELETE')
    ) s(Suffix, ActionName)
    WHERE v.MenuId IS NOT NULL;

    INSERT INTO dbo.Features (FeatureKey, FeatureName, Module, Description, CreatedDate)
    SELECT CONCAT(N'MENU_', seed.MenuId, seed.Suffix),
           CASE
               WHEN seed.Suffix = N'' THEN CONCAT(N'Menu: ', seed.Title)
               ELSE CONCAT(seed.ActionName, N' ', seed.Title)
           END,
           N'Menu Access',
           CASE
               WHEN seed.Suffix = N'' THEN CONCAT(N'Access to ', seed.Title, N' menu')
               ELSE CONCAT(seed.ActionName, N' permission for ', seed.Title, N' menu')
           END,
           SYSUTCDATETIME()
    FROM @MenuFeatureSeeds seed
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM dbo.Features existing
        WHERE existing.FeatureKey = CONCAT(N'MENU_', seed.MenuId, seed.Suffix)
    );

    DECLARE @EmployeeViewPermissionId int;
    SELECT TOP (1) @EmployeeViewPermissionId = PermissionId
    FROM dbo.Features
    WHERE FeatureKey = N'EMPLOYEE_VIEW';

    IF @EmployeeViewPermissionId IS NOT NULL
    BEGIN
        INSERT INTO dbo.MenuPermissions (MenuId, PermissionId)
        SELECT menuId, @EmployeeViewPermissionId
        FROM (VALUES (@ProcessId), (@ReportId), (@TaskListId)) target(menuId)
        WHERE target.menuId IS NOT NULL
          AND NOT EXISTS
          (
              SELECT 1
              FROM dbo.MenuPermissions existing
              WHERE existing.MenuId = target.menuId
                AND existing.PermissionId = @EmployeeViewPermissionId
          );
    END

    DECLARE @AccessTargets TABLE (MenuId int NOT NULL);
    INSERT INTO @AccessTargets (MenuId)
    SELECT target.MenuId
    FROM (VALUES (@ReportId), (@TaskListId)) target(MenuId)
    WHERE target.MenuId IS NOT NULL;

    INSERT INTO dbo.StaffMenuAccess (StaffId, MenuId, IsAllow, GrantedBy, GrantedDate)
    SELECT source.StaffId,
           target.MenuId,
           source.IsAllow,
           source.GrantedBy,
           source.GrantedDate
    FROM dbo.StaffMenuAccess source
    CROSS JOIN @AccessTargets target
    WHERE source.MenuId = @ProcessId
      AND NOT EXISTS
      (
          SELECT 1
          FROM dbo.StaffMenuAccess existing
          WHERE existing.StaffId = source.StaffId
            AND existing.MenuId = target.MenuId
      );

    INSERT INTO dbo.AccessFeatures (StaffMenuAccessId, PermissionId, IsAllow)
    SELECT targetAccess.Id,
           targetFeature.PermissionId,
           sourceFeature.IsAllow
    FROM dbo.StaffMenuAccess sourceAccess
    INNER JOIN dbo.AccessFeatures sourceFeature
        ON sourceFeature.StaffMenuAccessId = sourceAccess.Id
    INNER JOIN dbo.Features sourceFeatureRow
        ON sourceFeatureRow.PermissionId = sourceFeature.PermissionId
    INNER JOIN dbo.StaffMenuAccess targetAccess
        ON targetAccess.StaffId = sourceAccess.StaffId
       AND targetAccess.MenuId IN (SELECT MenuId FROM @AccessTargets)
    INNER JOIN dbo.Features targetFeature
        ON targetFeature.FeatureKey = CASE
            WHEN sourceFeatureRow.FeatureKey LIKE N'%_VIEW' THEN CONCAT(N'MENU_', targetAccess.MenuId, N'_VIEW')
            WHEN sourceFeatureRow.FeatureKey LIKE N'%_ADD' THEN CONCAT(N'MENU_', targetAccess.MenuId, N'_ADD')
            WHEN sourceFeatureRow.FeatureKey LIKE N'%_EDIT' THEN CONCAT(N'MENU_', targetAccess.MenuId, N'_EDIT')
            WHEN sourceFeatureRow.FeatureKey LIKE N'%_DELETE' THEN CONCAT(N'MENU_', targetAccess.MenuId, N'_DELETE')
            ELSE CONCAT(N'MENU_', targetAccess.MenuId)
        END
    WHERE sourceAccess.MenuId = @ProcessId
      AND NOT EXISTS
      (
          SELECT 1
          FROM dbo.AccessFeatures existing
          WHERE existing.StaffMenuAccessId = targetAccess.Id
            AND existing.PermissionId = targetFeature.PermissionId
      );

    INSERT INTO dbo.TenantMenuPermissions
        (TenantId, MenuId, IsAllow, CanView, CanAdd, CanEdit, CanDelete, GrantedOnUtc, GrantedByUserId)
    SELECT source.TenantId,
           target.MenuId,
           source.IsAllow,
           source.CanView,
           source.CanAdd,
           source.CanEdit,
           source.CanDelete,
           SYSUTCDATETIME(),
           COALESCE(source.GrantedByUserId, N'System: HR Process Menu')
    FROM dbo.TenantMenuPermissions source
    CROSS JOIN @AccessTargets target
    WHERE source.MenuId = @ProcessId
      AND NOT EXISTS
      (
          SELECT 1
          FROM dbo.TenantMenuPermissions existing
          WHERE existing.TenantId = source.TenantId
            AND existing.MenuId = target.MenuId
      );
END
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DECLARE @HrId int;
SELECT TOP (1) @HrId = Id
FROM dbo.Menus
WHERE Title = N'HR Management' AND ParentId IS NULL
ORDER BY SortOrder, Id;

IF @HrId IS NOT NULL
BEGIN
    DECLARE @ProcessId int;
    DECLARE @ReportId int;
    DECLARE @TaskListId int;

    SELECT TOP (1) @ProcessId = Id
    FROM dbo.Menus
    WHERE ParentId = @HrId AND Title = N'Process'
    ORDER BY Id;

    IF @ProcessId IS NOT NULL
    BEGIN
        SELECT TOP (1) @ReportId = Id
        FROM dbo.Menus
        WHERE ParentId = @ProcessId AND Route = N'/hr/process/report'
        ORDER BY Id;

        SELECT TOP (1) @TaskListId = Id
        FROM dbo.Menus
        WHERE ParentId = @ProcessId AND Route = N'/hr/process/task-list'
        ORDER BY Id;

        IF @TaskListId IS NOT NULL
        BEGIN
            DELETE FROM dbo.MenuPermissions WHERE MenuId = @TaskListId;
            DELETE featureAccess
            FROM dbo.AccessFeatures featureAccess
            INNER JOIN dbo.StaffMenuAccess staffAccess
                ON staffAccess.Id = featureAccess.StaffMenuAccessId
            WHERE staffAccess.MenuId = @TaskListId;
            DELETE FROM dbo.StaffMenuAccess WHERE MenuId = @TaskListId;
            DELETE FROM dbo.TenantMenuPermissions WHERE MenuId = @TaskListId;
            DELETE FROM dbo.Menus WHERE Id = @TaskListId;
        END

        IF @ReportId IS NOT NULL
        BEGIN
            DELETE FROM dbo.MenuPermissions WHERE MenuId = @ReportId;
            DELETE featureAccess
            FROM dbo.AccessFeatures featureAccess
            INNER JOIN dbo.StaffMenuAccess staffAccess
                ON staffAccess.Id = featureAccess.StaffMenuAccessId
            WHERE staffAccess.MenuId = @ReportId;
            DELETE FROM dbo.StaffMenuAccess WHERE MenuId = @ReportId;
            DELETE FROM dbo.TenantMenuPermissions WHERE MenuId = @ReportId;
            DELETE FROM dbo.Menus WHERE Id = @ReportId;
        END

        UPDATE dbo.Menus
        SET Title = N'Reports',
            Icon = N'BarChart2',
            Route = N'/hr/reports',
            ParentId = @HrId,
            SortOrder = 4,
            IsActive = 1
        WHERE Id = @ProcessId;
    END
END
");
        }
    }
}
