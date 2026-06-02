using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class AddAppNoteUserStates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppNoteUserStates",
                columns: table => new
                {
                    AppNoteUserStateId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NoteId = table.Column<int>(type: "int", nullable: false),
                    StaffId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsRead = table.Column<bool>(type: "bit", nullable: false),
                    IsAcknowledged = table.Column<bool>(type: "bit", nullable: false),
                    IsDismissed = table.Column<bool>(type: "bit", nullable: false),
                    ReadOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AcknowledgedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DismissedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppNoteUserStates", x => x.AppNoteUserStateId);
                    table.ForeignKey(
                        name: "FK_AppNoteUserStates_AppNotes_NoteId",
                        column: x => x.NoteId,
                        principalTable: "AppNotes",
                        principalColumn: "NoteId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppNoteUserStates_NoteId_StaffId",
                table: "AppNoteUserStates",
                columns: new[] { "NoteId", "StaffId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppNoteUserStates");
        }
    }
}
