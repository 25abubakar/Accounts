using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    [Migration("20260729145500_ForceHrProcessMenuStructure")]
    public partial class ForceHrProcessMenuStructure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
DECLARE @HrId int;
DECLARE @ProcessId int;
DECLARE @ReportsId int;
DECLARE @TaskListId int;

SELECT TOP (1) @HrId = Id
FROM dbo.Menus
WHERE ParentId IS NULL AND Title = N'HR Management'
ORDER BY Id;

IF @HrId IS NOT NULL
BEGIN
    SELECT TOP (1) @ProcessId = Id
    FROM dbo.Menus
    WHERE ParentId = @HrId AND Title = N'Process'
    ORDER BY Id;

    IF @ProcessId IS NULL
    BEGIN
        SELECT TOP (1) @ProcessId = Id
        FROM dbo.Menus
        WHERE ParentId = @HrId AND (Route = N'/hr/reports' OR Title = N'Reports')
        ORDER BY CASE WHEN Route = N'/hr/reports' THEN 0 ELSE 1 END, Id;

        IF @ProcessId IS NOT NULL
        BEGIN
            UPDATE dbo.Menus
            SET Title = N'Process',
                Icon = N'Workflow',
                Route = NULL,
                ParentId = @HrId,
                SortOrder = 4,
                IsActive = 1
            WHERE Id = @ProcessId;
        END
        ELSE
        BEGIN
            INSERT dbo.Menus (Title, Icon, Route, ParentId, SortOrder, IsActive)
            VALUES (N'Process', N'Workflow', NULL, @HrId, 4, 1);

            SET @ProcessId = CONVERT(int, SCOPE_IDENTITY());
        END
    END
    ELSE
    BEGIN
        UPDATE dbo.Menus
        SET Icon = N'Workflow',
            Route = NULL,
            ParentId = @HrId,
            SortOrder = 4,
            IsActive = 1
        WHERE Id = @ProcessId;
    END

    SELECT TOP (1) @ReportsId = Id
    FROM dbo.Menus
    WHERE Route IN (N'/hr/process/report', N'/hr/reports')
       OR (Title IN (N'Report', N'Reports') AND ParentId IN (@HrId, @ProcessId))
    ORDER BY CASE WHEN Route = N'/hr/process/report' THEN 0 WHEN Route = N'/hr/reports' THEN 1 ELSE 2 END, Id;

    IF @ReportsId IS NULL
    BEGIN
        INSERT dbo.Menus (Title, Icon, Route, ParentId, SortOrder, IsActive)
        VALUES (N'Reports', N'BarChart2', N'/hr/process/report', @ProcessId, 1, 1);

        SET @ReportsId = CONVERT(int, SCOPE_IDENTITY());
    END

    UPDATE dbo.Menus
    SET Title = N'Reports',
        Icon = N'BarChart2',
        Route = N'/hr/process/report',
        ParentId = @ProcessId,
        SortOrder = 1,
        IsActive = 1
    WHERE Id = @ReportsId;

    SELECT TOP (1) @TaskListId = Id
    FROM dbo.Menus
    WHERE Route = N'/hr/process/task-list'
       OR (Title = N'Task List' AND ParentId = @ProcessId)
    ORDER BY CASE WHEN Route = N'/hr/process/task-list' THEN 0 ELSE 1 END, Id;

    IF @TaskListId IS NULL
    BEGIN
        INSERT dbo.Menus (Title, Icon, Route, ParentId, SortOrder, IsActive)
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

    INSERT dbo.MenuPermissions (MenuId, PermissionId)
    SELECT target.MenuId, source.PermissionId
    FROM dbo.MenuPermissions source
    CROSS JOIN (VALUES (@ReportsId), (@TaskListId)) target(MenuId)
    WHERE source.MenuId = @ProcessId
      AND NOT EXISTS (
          SELECT 1 FROM dbo.MenuPermissions existing
          WHERE existing.MenuId = target.MenuId
            AND existing.PermissionId = source.PermissionId
      );

    UPDATE accessRows
    SET accessRows.MenuId = @ReportsId
    FROM dbo.StaffMenuAccess accessRows
    INNER JOIN dbo.Menus menuRow ON menuRow.Id = accessRows.MenuId
    WHERE menuRow.ParentId = @HrId
      AND menuRow.Id <> @ProcessId
      AND (menuRow.Route = N'/hr/reports' OR menuRow.Title IN (N'Report', N'Reports'));

    UPDATE accessRows
    SET accessRows.MenuId = @ReportsId
    FROM dbo.TenantMenuPermissions accessRows
    INNER JOIN dbo.Menus menuRow ON menuRow.Id = accessRows.MenuId
    WHERE menuRow.ParentId = @HrId
      AND menuRow.Id <> @ProcessId
      AND (menuRow.Route = N'/hr/reports' OR menuRow.Title IN (N'Report', N'Reports'));

    UPDATE dbo.Menus
    SET ParentId = @ProcessId,
        Route = N'/hr/process/report',
        Title = N'Reports',
        Icon = N'BarChart2',
        SortOrder = 1
    WHERE ParentId = @HrId
      AND Id <> @ProcessId
      AND (Route = N'/hr/reports' OR Title IN (N'Report', N'Reports'));

    ;WITH duplicateReports AS
    (
        SELECT Id,
               ROW_NUMBER() OVER (ORDER BY CASE WHEN Id = @ReportsId THEN 0 ELSE 1 END, Id) AS rn
        FROM dbo.Menus
        WHERE ParentId = @ProcessId
          AND (Route = N'/hr/process/report' OR Title IN (N'Report', N'Reports'))
    )
    UPDATE menuRow
    SET IsActive = CASE WHEN duplicateReports.rn = 1 THEN 1 ELSE 0 END,
        Route = CASE WHEN duplicateReports.rn = 1 THEN N'/hr/process/report' ELSE NULL END
    FROM dbo.Menus menuRow
    INNER JOIN duplicateReports ON duplicateReports.Id = menuRow.Id;

    ;WITH duplicateTasks AS
    (
        SELECT Id,
               ROW_NUMBER() OVER (ORDER BY CASE WHEN Id = @TaskListId THEN 0 ELSE 1 END, Id) AS rn
        FROM dbo.Menus
        WHERE ParentId = @ProcessId
          AND (Route = N'/hr/process/task-list' OR Title = N'Task List')
    )
    UPDATE menuRow
    SET IsActive = CASE WHEN duplicateTasks.rn = 1 THEN 1 ELSE 0 END,
        Route = CASE WHEN duplicateTasks.rn = 1 THEN N'/hr/process/task-list' ELSE NULL END
    FROM dbo.Menus menuRow
    INNER JOIN duplicateTasks ON duplicateTasks.Id = menuRow.Id;
END
""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
DECLARE @HrId int;
DECLARE @ProcessId int;

SELECT TOP (1) @HrId = Id
FROM dbo.Menus
WHERE ParentId IS NULL AND Title = N'HR Management'
ORDER BY Id;

SELECT TOP (1) @ProcessId = Id
FROM dbo.Menus
WHERE ParentId = @HrId AND Title = N'Process'
ORDER BY Id;

IF @ProcessId IS NOT NULL
BEGIN
    DELETE FROM dbo.Menus
    WHERE ParentId = @ProcessId AND Route = N'/hr/process/task-list';

    DELETE FROM dbo.Menus
    WHERE ParentId = @ProcessId AND Route = N'/hr/process/report';

    UPDATE dbo.Menus
    SET Title = N'Reports',
        Icon = N'BarChart2',
        Route = N'/hr/reports',
        ParentId = @HrId,
        SortOrder = 4,
        IsActive = 1
    WHERE Id = @ProcessId;
END
""");
        }
    }
}
