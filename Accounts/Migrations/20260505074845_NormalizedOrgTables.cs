using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class NormalizedOrgTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Tables (Countries, Companies, Branches, Roles, Staff)
            // were already created manually via SQL script.
            // This migration just registers the EF model in __EFMigrationsHistory.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "Staff");
            migrationBuilder.DropTable(name: "Branches");
            migrationBuilder.DropTable(name: "Roles");
            migrationBuilder.DropTable(name: "Companies");
            migrationBuilder.DropTable(name: "Countries");
        }
    }
}
