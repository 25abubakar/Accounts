using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class AddAllowanceCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AllowanceCategory",
                schema: "PlatformTypes",
                table: "AllowanceTypes",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "APPT");

            migrationBuilder.CreateIndex(
                name: "IX_AllowanceTypes_TenantId_AllowanceCategory_DisplayOrder",
                schema: "PlatformTypes",
                table: "AllowanceTypes",
                columns: new[] { "TenantId", "AllowanceCategory", "DisplayOrder" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AllowanceTypes_TenantId_AllowanceCategory_DisplayOrder",
                schema: "PlatformTypes",
                table: "AllowanceTypes");

            migrationBuilder.DropColumn(
                name: "AllowanceCategory",
                schema: "PlatformTypes",
                table: "AllowanceTypes");
        }
    }
}
