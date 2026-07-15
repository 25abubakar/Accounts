using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonShiftSchedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ShiftEndTime",
                table: "Persons",
                type: "nvarchar(5)",
                maxLength: 5,
                nullable: false,
                defaultValue: "18:00");

            migrationBuilder.AddColumn<string>(
                name: "ShiftStartTime",
                table: "Persons",
                type: "nvarchar(5)",
                maxLength: 5,
                nullable: false,
                defaultValue: "09:00");

            migrationBuilder.AddColumn<string>(
                name: "TimeZoneId",
                table: "Persons",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "Asia/Karachi");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ShiftEndTime",
                table: "Persons");

            migrationBuilder.DropColumn(
                name: "ShiftStartTime",
                table: "Persons");

            migrationBuilder.DropColumn(
                name: "TimeZoneId",
                table: "Persons");

        }
    }
}
