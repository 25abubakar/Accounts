using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class AddProcessActionAuthorities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProcessActionAuthorities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    ProcessCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ActionCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    StaffId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CreatedDateUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessActionAuthorities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProcessActionAuthorities_StaffVacancy_StaffId",
                        column: x => x.StaffId,
                        principalTable: "StaffVacancy",
                        principalColumn: "StaffId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProcessActionAuthorities_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProcessActionAuthorities_StaffId",
                table: "ProcessActionAuthorities",
                column: "StaffId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessActionAuthorities_TenantId_ProcessCode_ActionCode_IsActive",
                table: "ProcessActionAuthorities",
                columns: new[] { "TenantId", "ProcessCode", "ActionCode", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_ProcessActionAuthorities_TenantId_ProcessCode_ActionCode_StaffId",
                table: "ProcessActionAuthorities",
                columns: new[] { "TenantId", "ProcessCode", "ActionCode", "StaffId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProcessActionAuthorities");
        }
    }
}
