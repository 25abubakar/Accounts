using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260717170000_AddAttendancePortalEmptyChildMenus")]
public sealed class AddAttendancePortalEmptyChildMenus : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DECLARE @ParentId int = (
                SELECT TOP (1) [Id] FROM [Menus]
                WHERE [Title] = N'Attendance Portal' AND [ParentId] IS NULL
                ORDER BY [Id]
            );

            IF @ParentId IS NOT NULL
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM [Menus] WHERE [Route] = N'/attendance/team')
                    INSERT INTO [Menus] ([Title], [Icon], [Route], [ParentId], [SortOrder], [IsActive])
                    VALUES (N'Team Attendance', N'Users', N'/attendance/team', @ParentId, 3, 1);

                IF NOT EXISTS (SELECT 1 FROM [Menus] WHERE [Route] = N'/attendance/monthly-chart')
                    INSERT INTO [Menus] ([Title], [Icon], [Route], [ParentId], [SortOrder], [IsActive])
                    VALUES (N'Monthly Chart', N'CalendarRange', N'/attendance/monthly-chart', @ParentId, 4, 1);
            END
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DELETE FROM [Menus]
            WHERE [Route] IN (N'/attendance/team', N'/attendance/monthly-chart');
            """);
    }
}
