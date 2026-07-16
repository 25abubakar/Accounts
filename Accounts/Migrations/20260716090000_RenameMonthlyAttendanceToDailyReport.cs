using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260716090000_RenameMonthlyAttendanceToDailyReport")]
public sealed class RenameMonthlyAttendanceToDailyReport : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE [Menus]
            SET [Title] = N'Daily Attendance Report', [Route] = N'/attendance/daily-report', [Icon] = N'CalendarDays'
            WHERE [Route] = N'/attendance/monthly-report';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE [Menus]
            SET [Title] = N'Monthly Attendance Report', [Route] = N'/attendance/monthly-report', [Icon] = N'CalendarRange'
            WHERE [Route] = N'/attendance/daily-report';
            """);
    }
}
