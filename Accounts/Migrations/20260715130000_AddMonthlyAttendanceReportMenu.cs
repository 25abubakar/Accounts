using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260715130000_AddMonthlyAttendanceReportMenu")]
public sealed class AddMonthlyAttendanceReportMenu : Migration
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
               AND NOT EXISTS (SELECT 1 FROM [Menus] WHERE [Route] = N'/attendance/monthly-report')
            BEGIN
                INSERT INTO [Menus] ([Title], [Icon], [Route], [ParentId], [SortOrder], [IsActive])
                VALUES (N'Monthly Attendance Report', N'CalendarRange', N'/attendance/monthly-report', @ParentId, 2, 1);
            END
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DECLARE @MenuId int = (SELECT TOP (1) [Id] FROM [Menus] WHERE [Route] = N'/attendance/monthly-report');
            IF @MenuId IS NOT NULL
            BEGIN
                DELETE FROM [MenuPermissions] WHERE [MenuId] = @MenuId;
                DELETE FROM [StaffMenuAccess] WHERE [MenuId] = @MenuId;
                DELETE FROM [TenantMenuPermissions] WHERE [MenuId] = @MenuId;
                DELETE FROM [Menus] WHERE [Id] = @MenuId;
            END
            """);
    }
}
