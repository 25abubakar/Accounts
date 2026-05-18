using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class AddHierarchicalRBAC : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // RolePermissions and UserPermissionOverrides tables already
            // created directly via SQL. This migration just registers the
            // EF model in __EFMigrationsHistory.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "UserPermissionOverrides");
            migrationBuilder.DropTable(name: "RolePermissions");
        }
    }
}
