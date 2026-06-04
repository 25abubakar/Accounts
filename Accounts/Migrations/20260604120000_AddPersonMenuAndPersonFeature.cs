using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    [Migration("20260604120000_AddPersonMenuAndPersonFeature")]
    public partial class AddPersonMenuAndPersonFeature : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PersonMenus",
                columns: table => new
                {
                    PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MenuId = table.Column<int>(type: "int", nullable: false),
                    GrantedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    GrantedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersonMenus", x => new { x.PersonId, x.MenuId });
                    table.ForeignKey(
                        name: "FK_PersonMenus_Menus_MenuId",
                        column: x => x.MenuId,
                        principalTable: "Menus",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PersonMenus_Persons_PersonId",
                        column: x => x.PersonId,
                        principalTable: "Persons",
                        principalColumn: "PersonId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PersonFeatures",
                columns: table => new
                {
                    PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PermissionId = table.Column<int>(type: "int", nullable: false),
                    GrantedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    GrantedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersonFeatures", x => new { x.PersonId, x.PermissionId });
                    table.ForeignKey(
                        name: "FK_PersonFeatures_Features_PermissionId",
                        column: x => x.PermissionId,
                        principalTable: "Features",
                        principalColumn: "PermissionId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PersonFeatures_Persons_PersonId",
                        column: x => x.PersonId,
                        principalTable: "Persons",
                        principalColumn: "PersonId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PersonMenus_MenuId",
                table: "PersonMenus",
                column: "MenuId");

            migrationBuilder.CreateIndex(
                name: "IX_PersonMenus_PersonId",
                table: "PersonMenus",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_PersonFeatures_PermissionId",
                table: "PersonFeatures",
                column: "PermissionId");

            migrationBuilder.CreateIndex(
                name: "IX_PersonFeatures_PersonId",
                table: "PersonFeatures",
                column: "PersonId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "PersonFeatures");
            migrationBuilder.DropTable(name: "PersonMenus");
        }
    }
}
