using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonReportToHierarchy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ReportsToPersonId",
                table: "Persons",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Persons_ReportsToPersonId",
                table: "Persons",
                column: "ReportsToPersonId");

            migrationBuilder.AddForeignKey(
                name: "FK_Persons_Persons_ReportsToPersonId",
                table: "Persons",
                column: "ReportsToPersonId",
                principalTable: "Persons",
                principalColumn: "PersonId",
                onDelete: ReferentialAction.NoAction);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Persons_Persons_ReportsToPersonId",
                table: "Persons");

            migrationBuilder.DropIndex(
                name: "IX_Persons_ReportsToPersonId",
                table: "Persons");

            migrationBuilder.DropColumn(
                name: "ReportsToPersonId",
                table: "Persons");
        }
    }
}
