using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class AddAppNoteOwnerIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OwnerIdentityUserId",
                table: "AppNotes",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppNotes_OwnerIdentityUserId",
                table: "AppNotes",
                column: "OwnerIdentityUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_AppNotes_AspNetUsers_OwnerIdentityUserId",
                table: "AppNotes",
                column: "OwnerIdentityUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // Backfill existing personal notes with their creator identity.
            migrationBuilder.Sql(@"
UPDATE n
SET n.OwnerIdentityUserId = n.CreatedBy
FROM AppNotes n
INNER JOIN AspNetUsers u ON u.Id = n.CreatedBy
WHERE n.SourceTypeCode = 'USER'
  AND n.OwnerIdentityUserId IS NULL
  AND n.CreatedBy IS NOT NULL;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AppNotes_AspNetUsers_OwnerIdentityUserId",
                table: "AppNotes");

            migrationBuilder.DropIndex(
                name: "IX_AppNotes_OwnerIdentityUserId",
                table: "AppNotes");

            migrationBuilder.DropColumn(
                name: "OwnerIdentityUserId",
                table: "AppNotes");
        }
    }
}
