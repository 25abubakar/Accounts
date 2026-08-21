using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendanceAdjustment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AdjustmentAmount",
                table: "AttendanceMonthlySettlements",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AdjustmentRemarks",
                table: "AttendanceMonthlySettlements",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AdjustmentAmount",
                table: "AttendanceMonthlySettlements");

            migrationBuilder.DropColumn(
                name: "AdjustmentRemarks",
                table: "AttendanceMonthlySettlements");
        }
    }
}
