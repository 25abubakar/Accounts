using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260731084100_AddAlternativeReportToPerson")]
    public partial class AddAlternativeReportToPerson : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AlternativeReportsToPersonId",
                table: "Persons",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Persons_AlternativeReportsToPersonId",
                table: "Persons",
                column: "AlternativeReportsToPersonId");

            migrationBuilder.AddForeignKey(
                name: "FK_Persons_Persons_AlternativeReportsToPersonId",
                table: "Persons",
                column: "AlternativeReportsToPersonId",
                principalTable: "Persons",
                principalColumn: "PersonId",
                onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Persons_Persons_AlternativeReportsToPersonId",
                table: "Persons");

            migrationBuilder.DropIndex(
                name: "IX_Persons_AlternativeReportsToPersonId",
                table: "Persons");

            migrationBuilder.DropColumn(
                name: "AlternativeReportsToPersonId",
                table: "Persons");
        }
    }
}
