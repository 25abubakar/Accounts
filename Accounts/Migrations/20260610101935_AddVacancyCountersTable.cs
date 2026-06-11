using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class AddVacancyCountersTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Baqi saare dropped aur conflict wale tables ko hata kar 
            // yahan sirf aapka missing VacancyCounters table add kar diya hai.
            migrationBuilder.CreateTable(
                name: "VacancyCounters",
                columns: table => new
                {
                    Prefix = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    LastNumber = table.Column<int>(type: "int", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VacancyCounters", x => x.Prefix);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VacancyCounters");
        }
    }
}