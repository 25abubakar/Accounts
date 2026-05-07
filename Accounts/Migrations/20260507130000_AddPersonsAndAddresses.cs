using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonsAndAddresses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── 1. Create Persons table ───────────────────────────────
            migrationBuilder.CreateTable(
                name: "Persons",
                columns: table => new
                {
                    PersonId       = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    FullName       = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Phone          = table.Column<string>(type: "nvarchar(50)",  maxLength: 50,  nullable: true),
                    Email          = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Gender         = table.Column<string>(type: "nvarchar(20)",  maxLength: 20,  nullable: true),
                    DateOfBirth    = table.Column<DateTime>(type: "datetime2",   nullable: true),
                    MaritalStatus  = table.Column<string>(type: "nvarchar(50)",  maxLength: 50,  nullable: true),
                    ProfilePhotoUrl= table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    LoginId        = table.Column<string>(type: "nvarchar(30)",  maxLength: 30,  nullable: false),
                    IdentityUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CreatedDate    = table.Column<DateTime>(type: "datetime2",   nullable: false, defaultValueSql: "GETDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Persons", x => x.PersonId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Persons_LoginId",
                table: "Persons",
                column: "LoginId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Persons_IdentityUserId",
                table: "Persons",
                column: "IdentityUserId",
                unique: true);

            // ── 2. Create PersonAddresses table ───────────────────────
            migrationBuilder.CreateTable(
                name: "PersonAddresses",
                columns: table => new
                {
                    AddressId   = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    PersonId    = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AddressType = table.Column<string>(type: "nvarchar(20)",  maxLength: 20,  nullable: false),
                    AddressLine = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    Country     = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Province    = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    District    = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    City        = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PostalCode  = table.Column<string>(type: "nvarchar(20)",  maxLength: 20,  nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersonAddresses", x => x.AddressId);
                    table.ForeignKey(
                        name: "FK_PersonAddresses_Persons_PersonId",
                        column: x => x.PersonId,
                        principalTable: "Persons",
                        principalColumn: "PersonId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PersonAddresses_PersonId_AddressType",
                table: "PersonAddresses",
                columns: new[] { "PersonId", "AddressType" },
                unique: true);

            // ── 3. Add PersonId column to Staff ───────────────────────
            migrationBuilder.AddColumn<Guid>(
                name: "PersonId",
                table: "Staff",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Staff_PersonId",
                table: "Staff",
                column: "PersonId",
                unique: true,
                filter: "[PersonId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_Staff_Persons_PersonId",
                table: "Staff",
                column: "PersonId",
                principalTable: "Persons",
                principalColumn: "PersonId",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Staff_Persons_PersonId",
                table: "Staff");

            migrationBuilder.DropIndex(
                name: "IX_Staff_PersonId",
                table: "Staff");

            migrationBuilder.DropColumn(
                name: "PersonId",
                table: "Staff");

            migrationBuilder.DropTable(name: "PersonAddresses");
            migrationBuilder.DropTable(name: "Persons");
        }
    }
}
