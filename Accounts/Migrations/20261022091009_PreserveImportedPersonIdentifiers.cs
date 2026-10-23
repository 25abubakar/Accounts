using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class PreserveImportedPersonIdentifiers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LegacyPersonId",
                table: "Persons",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Persons_TenantId_LegacyPersonId",
                table: "Persons",
                columns: new[] { "TenantId", "LegacyPersonId" },
                unique: true,
                filter: "[LegacyPersonId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM dbo.Persons WHERE LegacyPersonId IS NOT NULL) THROW 51000, 'Original person identifiers are populated; export and explicitly migrate them before rolling back.', 1;");
            migrationBuilder.DropIndex(
                name: "IX_Persons_TenantId_LegacyPersonId",
                table: "Persons");

            migrationBuilder.DropColumn(
                name: "LegacyPersonId",
                table: "Persons");
        }
    }
}
