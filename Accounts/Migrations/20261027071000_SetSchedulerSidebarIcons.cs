using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace Accounts.Migrations;
[DbContext(typeof(ApplicationDbContext))]
[Migration("20261027071000_SetSchedulerSidebarIcons")]
public sealed class SetSchedulerSidebarIcons : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)=>migrationBuilder.Sql("""
        UPDATE dbo.Menus SET Icon=N'Clock3' WHERE ParentId IS NULL AND Title=N'Scheduler';
        UPDATE dbo.Menus SET Icon=N'CalendarRange' WHERE Route=N'/scheduler/jobs';
        UPDATE dbo.Menus SET Icon=N'ListChecks' WHERE Route=N'/scheduler/history';
        """);
    protected override void Down(MigrationBuilder migrationBuilder){ }
}
