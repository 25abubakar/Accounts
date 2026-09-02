using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class AddPayrollBonusDistribution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PayrollBonusDistributions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    BenefitParameterId = table.Column<int>(type: "int", nullable: false),
                    Month = table.Column<int>(type: "int", nullable: true),
                    BasicPercentage = table.Column<decimal>(type: "decimal(9,4)", nullable: false),
                    ServicePercentage = table.Column<decimal>(type: "decimal(9,4)", nullable: false),
                    ServiceYears = table.Column<decimal>(type: "decimal(9,2)", nullable: false),
                    AssessmentPercentage = table.Column<decimal>(type: "decimal(9,4)", nullable: false),
                    AttendancePercentage = table.Column<decimal>(type: "decimal(9,4)", nullable: false),
                    LeavePercentage = table.Column<decimal>(type: "decimal(9,4)", nullable: false),
                    DisciplinePercentage = table.Column<decimal>(type: "decimal(9,4)", nullable: false),
                    Installments = table.Column<int>(type: "int", nullable: false),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollBonusDistributions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollBonusDistributions_PayrollBenefitParameters_BenefitParameterId",
                        column: x => x.BenefitParameterId,
                        principalTable: "PayrollBenefitParameters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PayrollBonusDistributions_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollBonusDistributions_BenefitParameterId",
                table: "PayrollBonusDistributions",
                column: "BenefitParameterId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollBonusDistributions_TenantId_BenefitParameterId",
                table: "PayrollBonusDistributions",
                columns: new[] { "TenantId", "BenefitParameterId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PayrollBonusDistributions");
        }
    }
}
