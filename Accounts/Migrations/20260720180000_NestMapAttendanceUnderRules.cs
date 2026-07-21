using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260720180000_NestMapAttendanceUnderRules")]
public sealed class NestMapAttendanceUnderRules : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DECLARE @AttendancePortalId int = (
                SELECT TOP (1) [Id]
                FROM [Menus]
                WHERE [Title] = N'Attendance Portal' AND [ParentId] IS NULL
                ORDER BY [Id]
            );
            DECLARE @AttendanceRulesId int = (
                SELECT TOP (1) [Id]
                FROM [Menus]
                WHERE [ParentId] = @AttendancePortalId AND [Title] = N'Attendance Rules'
                ORDER BY [Id]
            );
            DECLARE @RulesListId int = (
                SELECT TOP (1) [Id]
                FROM [Menus]
                WHERE [Route] IN (N'/attendance/rules/rule', N'/attendance/rules/list')
                   OR ([ParentId] = @AttendanceRulesId AND [Title] IN (N'Rule', N'Rules List'))
                ORDER BY CASE WHEN [Route] = N'/attendance/rules/list' THEN 0 ELSE 1 END, [Id]
            );
            DECLARE @MapAttendanceId int = (
                SELECT TOP (1) [Id]
                FROM [Menus]
                WHERE [Route] IN (N'/attendance/map-attendance', N'/attendance/rules/map-attendance')
                   OR [Title] = N'Map Attendance'
                ORDER BY CASE WHEN [Route] = N'/attendance/rules/map-attendance' THEN 0 ELSE 1 END, [Id]
            );

            IF @AttendanceRulesId IS NOT NULL
                UPDATE [Menus]
                SET [Route] = NULL, [ParentId] = @AttendancePortalId, [SortOrder] = 6, [IsActive] = 1
                WHERE [Id] = @AttendanceRulesId;

            IF @RulesListId IS NOT NULL AND @AttendanceRulesId IS NOT NULL
                UPDATE [Menus]
                SET [Title] = N'Rules List', [Icon] = N'ListChecks', [Route] = N'/attendance/rules/list',
                    [ParentId] = @AttendanceRulesId, [SortOrder] = 1, [IsActive] = 1
                WHERE [Id] = @RulesListId;

            IF @MapAttendanceId IS NOT NULL AND @AttendanceRulesId IS NOT NULL
                UPDATE [Menus]
                SET [Title] = N'Map Attendance', [Icon] = N'MapPin', [Route] = N'/attendance/rules/map-attendance',
                    [ParentId] = @AttendanceRulesId, [SortOrder] = 2, [IsActive] = 1
                WHERE [Id] = @MapAttendanceId;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DECLARE @AttendancePortalId int = (
                SELECT TOP (1) [Id] FROM [Menus]
                WHERE [Title] = N'Attendance Portal' AND [ParentId] IS NULL ORDER BY [Id]
            );
            DECLARE @AttendanceRulesId int = (
                SELECT TOP (1) [Id] FROM [Menus]
                WHERE [ParentId] = @AttendancePortalId AND [Title] = N'Attendance Rules' ORDER BY [Id]
            );

            UPDATE [Menus]
            SET [Title] = N'Rule', [Route] = N'/attendance/rules/rule', [SortOrder] = 1
            WHERE [ParentId] = @AttendanceRulesId AND [Route] = N'/attendance/rules/list';

            UPDATE [Menus]
            SET [Route] = N'/attendance/map-attendance', [ParentId] = @AttendancePortalId, [SortOrder] = 7
            WHERE [ParentId] = @AttendanceRulesId AND [Route] = N'/attendance/rules/map-attendance';
            """);
    }
}
