using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class AddPayrollBenefitRulesAndParameters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PayrollBenefitRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    BenefitReference = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    BenefitsType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Company = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    Entitled = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    Contract = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Frequency = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    ValidFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    ValidTo = table.Column<DateOnly>(type: "date", nullable: true),
                    MaximumExpense = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ServiceStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Scale = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Wef = table.Column<DateOnly>(type: "date", nullable: true),
                    MinimumService = table.Column<decimal>(type: "decimal(9,2)", nullable: false),
                    MaximumPh = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    MinimumPh = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    IsIneligible = table.Column<bool>(type: "bit", nullable: false),
                    ShareType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    CompanyShare = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    StaffShare = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    OrganizationId = table.Column<int>(type: "int", nullable: true),
                    CompanyName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollBenefitRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollBenefitRules_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PayrollBenefitParameters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    BenefitRuleId = table.Column<int>(type: "int", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    PeriodFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    PeriodTo = table.Column<DateOnly>(type: "date", nullable: true),
                    MinimumService = table.Column<decimal>(type: "decimal(9,2)", nullable: false),
                    AmountType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PayType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CompanyShare = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    StaffShare = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollBenefitParameters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollBenefitParameters_PayrollBenefitRules_BenefitRuleId",
                        column: x => x.BenefitRuleId,
                        principalTable: "PayrollBenefitRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollBenefitParameters_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollBenefitParameters_BenefitRuleId",
                table: "PayrollBenefitParameters",
                column: "BenefitRuleId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollBenefitParameters_TenantId_BenefitRuleId_Name",
                table: "PayrollBenefitParameters",
                columns: new[] { "TenantId", "BenefitRuleId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollBenefitParameters_TenantId_Reference",
                table: "PayrollBenefitParameters",
                columns: new[] { "TenantId", "Reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollBenefitRules_TenantId_BenefitReference",
                table: "PayrollBenefitRules",
                columns: new[] { "TenantId", "BenefitReference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollBenefitRules_TenantId_Name",
                table: "PayrollBenefitRules",
                columns: new[] { "TenantId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PayrollBenefitParameters");

            migrationBuilder.DropTable(
                name: "PayrollBenefitRules");
        }
    }
}
