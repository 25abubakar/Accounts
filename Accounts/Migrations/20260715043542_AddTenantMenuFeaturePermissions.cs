using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantMenuFeaturePermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CanAdd",
                table: "TenantMenuPermissions",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "CanDelete",
                table: "TenantMenuPermissions",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "CanEdit",
                table: "TenantMenuPermissions",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "CanView",
                table: "TenantMenuPermissions",
                type: "bit",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CanAdd",
                table: "TenantMenuPermissions");

            migrationBuilder.DropColumn(
                name: "CanDelete",
                table: "TenantMenuPermissions");

            migrationBuilder.DropColumn(
                name: "CanEdit",
                table: "TenantMenuPermissions");

            migrationBuilder.DropColumn(
                name: "CanView",
                table: "TenantMenuPermissions");
        }
    }
}
