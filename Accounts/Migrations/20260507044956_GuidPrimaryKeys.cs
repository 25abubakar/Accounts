using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class GuidPrimaryKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop Staff first (FK → Vacancies)
            migrationBuilder.DropTable(name: "Staff");

            // Drop Vacancies
            migrationBuilder.DropTable(name: "Vacancies");

            // Recreate Vacancies with Guid PK
            migrationBuilder.CreateTable(
                name: "Vacancies",
                columns: table => new
                {
                    VacancyId     = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    VacancyCode   = table.Column<string>(type: "nvarchar(50)",  maxLength: 50,  nullable: false),
                    JobTitle      = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Department    = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsFilled      = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CreatedDate   = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETDATE()"),
                    OrganizationId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vacancies", x => x.VacancyId);
                    table.ForeignKey(
                        name: "FK_Vacancies_OrganizationTree_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "dbo",
                        principalTable: "OrganizationTree",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            // Recreate Staff with Guid PK and Guid FK
            migrationBuilder.CreateTable(
                name: "Staff",
                columns: table => new
                {
                    StaffId     = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    FullName    = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Email       = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Phone       = table.Column<string>(type: "nvarchar(50)",  maxLength: 50,  nullable: true),
                    PhotoUrl    = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    JoiningDate = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETDATE()"),
                    VacancyId   = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Staff", x => x.StaffId);
                    table.ForeignKey(
                        name: "FK_Staff_Vacancies_VacancyId",
                        column: x => x.VacancyId,
                        principalTable: "Vacancies",
                        principalColumn: "VacancyId",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Vacancies_OrganizationId",
                table: "Vacancies",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_Staff_VacancyId",
                table: "Staff",
                column: "VacancyId",
                unique: true,
                filter: "[VacancyId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "Staff");
            migrationBuilder.DropTable(name: "Vacancies");

            // Restore int-based tables
            migrationBuilder.CreateTable(
                name: "Vacancies",
                columns: table => new
                {
                    VacancyId     = table.Column<int>(type: "int", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                    VacancyCode   = table.Column<string>(type: "nvarchar(50)",  maxLength: 50,  nullable: false),
                    JobTitle      = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Department    = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsFilled      = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CreatedDate   = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETDATE()"),
                    OrganizationId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vacancies", x => x.VacancyId);
                    table.ForeignKey(
                        name: "FK_Vacancies_OrganizationTree_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "dbo",
                        principalTable: "OrganizationTree",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Staff",
                columns: table => new
                {
                    StaffId     = table.Column<int>(type: "int", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                    FullName    = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Email       = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Phone       = table.Column<string>(type: "nvarchar(50)",  maxLength: 50,  nullable: true),
                    PhotoUrl    = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    JoiningDate = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETDATE()"),
                    VacancyId   = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Staff", x => x.StaffId);
                    table.ForeignKey(
                        name: "FK_Staff_Vacancies_VacancyId",
                        column: x => x.VacancyId,
                        principalTable: "Vacancies",
                        principalColumn: "VacancyId",
                        onDelete: ReferentialAction.SetNull);
                });
        }
    }
}
