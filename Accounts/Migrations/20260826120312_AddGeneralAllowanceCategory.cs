using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class AddGeneralAllowanceCategory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE [PlatformTypes].[AllowanceTypes] SET [AllowanceCategory] = 'GENERAL' WHERE [AllowanceCategory] = 'APPT';");

            migrationBuilder.AlterColumn<string>(
                name: "AllowanceCategory",
                schema: "PlatformTypes",
                table: "AllowanceTypes",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "GENERAL",
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20,
                oldDefaultValue: "APPT");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE [PlatformTypes].[AllowanceTypes] SET [AllowanceCategory] = 'APPT' WHERE [AllowanceCategory] = 'GENERAL';");

            migrationBuilder.AlterColumn<string>(
                name: "AllowanceCategory",
                schema: "PlatformTypes",
                table: "AllowanceTypes",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "APPT",
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20,
                oldDefaultValue: "GENERAL");
        }
    }
}
