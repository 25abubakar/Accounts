using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class AllowWeekendWorkingScheduleOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_EmployeeTimingSchedules_RequiredWeekend",
                table: "EmployeeTimingSchedules");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_EmployeeTimingSchedules_RequiredWeekend",
                table: "EmployeeTimingSchedules",
                sql: "(((DATEDIFF(day,'19000101',[ScheduleDate]) % 7 + 7) % 7) NOT IN (5,6)) OR (((DATEDIFF(day,'19000101',[ScheduleDate]) % 7 + 7) % 7) IN (5,6) AND [IsOn] = 0 AND [TimeFrom] IS NULL AND [TimeTo] IS NULL AND [WorkingMinutes] = 0)");
        }
    }
}
