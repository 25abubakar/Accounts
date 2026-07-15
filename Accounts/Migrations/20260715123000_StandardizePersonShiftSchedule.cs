using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260715123000_StandardizePersonShiftSchedule")]
    public class StandardizePersonShiftSchedule : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE [Persons]
                SET [ShiftStartTime] = N'09:00',
                    [ShiftEndTime] = N'18:00',
                    [TimeZoneId] = N'Asia/Karachi';
                """);

            migrationBuilder.AlterColumn<string>(
                name: "ShiftStartTime",
                table: "Persons",
                type: "nvarchar(5)",
                maxLength: 5,
                nullable: false,
                defaultValue: "09:00",
                oldClrType: typeof(string),
                oldType: "nvarchar(5)",
                oldMaxLength: 5,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ShiftEndTime",
                table: "Persons",
                type: "nvarchar(5)",
                maxLength: 5,
                nullable: false,
                defaultValue: "18:00",
                oldClrType: typeof(string),
                oldType: "nvarchar(5)",
                oldMaxLength: 5,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "TimeZoneId",
                table: "Persons",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "Asia/Karachi",
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100,
                oldNullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ShiftStartTime",
                table: "Persons",
                type: "nvarchar(5)",
                maxLength: 5,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(5)",
                oldMaxLength: 5,
                oldDefaultValue: "09:00");

            migrationBuilder.AlterColumn<string>(
                name: "ShiftEndTime",
                table: "Persons",
                type: "nvarchar(5)",
                maxLength: 5,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(5)",
                oldMaxLength: 5,
                oldDefaultValue: "18:00");

            migrationBuilder.AlterColumn<string>(
                name: "TimeZoneId",
                table: "Persons",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100,
                oldDefaultValue: "Asia/Karachi");
        }
    }
}
