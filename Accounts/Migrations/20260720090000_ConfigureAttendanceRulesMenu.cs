using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260720090000_ConfigureAttendanceRulesMenu")]
public sealed class ConfigureAttendanceRulesMenu : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DECLARE @ParentId int = (
                SELECT TOP (1) [Id]
                FROM [Menus]
                WHERE [Title] = N'Attendance Portal' AND [ParentId] IS NULL
                ORDER BY [Id]
            );
            DECLARE @RulesMenuId int = (
                SELECT TOP (1) [Id]
                FROM [Menus]
                WHERE [ParentId] = @ParentId
                  AND (
                      [Route] = N'/attendance/rules'
                      OR [Route] = N'/settings/attendance-status'
                      OR [Title] IN (N'Rules List', N'Attendance Rules')
                  )
                ORDER BY CASE WHEN [Route] = N'/attendance/rules' THEN 0 ELSE 1 END, [Id]
            );

            IF @RulesMenuId IS NULL AND @ParentId IS NOT NULL
            BEGIN
                INSERT INTO [Menus] ([Title], [Icon], [Route], [ParentId], [SortOrder], [IsActive])
                VALUES (N'Attendance Rules', N'ListChecks', N'/attendance/rules', @ParentId, 6, 1);
                SET @RulesMenuId = SCOPE_IDENTITY();
            END;

            IF @RulesMenuId IS NOT NULL
            BEGIN
                UPDATE [Menus]
                SET [Title] = N'Attendance Rules',
                    [Icon] = N'ListChecks',
                    [Route] = N'/attendance/rules',
                    [ParentId] = @ParentId,
                    [SortOrder] = 6,
                    [IsActive] = 1
                WHERE [Id] = @RulesMenuId;

                UPDATE [TenantMenuPermissions]
                SET [IsAllow] = 1,
                    [CanView] = 1,
                    [CanAdd] = 1,
                    [CanEdit] = 1,
                    [CanDelete] = 1
                WHERE [MenuId] = @RulesMenuId;

                INSERT INTO [TenantMenuPermissions]
                    ([TenantId], [MenuId], [IsAllow], [CanView], [CanAdd], [CanEdit], [CanDelete],
                     [GrantedOnUtc], [GrantedByUserId])
                SELECT tenant.[Id], @RulesMenuId, 1, 1, 1, 1, 1,
                       SYSUTCDATETIME(), N'System: Attendance Rules'
                FROM [Tenants] tenant
                WHERE tenant.[IsActive] = 1
                  AND NOT EXISTS (
                      SELECT 1
                      FROM [TenantMenuPermissions] permission
                      WHERE permission.[TenantId] = tenant.[Id]
                        AND permission.[MenuId] = @RulesMenuId
                  );
            END;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DECLARE @RulesMenuId int = (
                SELECT TOP (1) [Id]
                FROM [Menus]
                WHERE [Route] = N'/attendance/rules'
                ORDER BY [Id]
            );

            IF @RulesMenuId IS NOT NULL
            BEGIN
                DELETE FROM [TenantMenuPermissions]
                WHERE [MenuId] = @RulesMenuId
                  AND [GrantedByUserId] = N'System: Attendance Rules';

                UPDATE [Menus]
                SET [Title] = N'Rules List',
                    [Icon] = N'CalendarCheck2',
                    [Route] = N'/settings/attendance-status',
                    [SortOrder] = 1
                WHERE [Id] = @RulesMenuId;
            END;
            """);
    }
}
