using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class AddPayrollBonusWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PayrollBonusRuns",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    BenefitRuleId = table.Column<int>(type: "int", nullable: false),
                    RunNumber = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    BenefitReference = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    RuleName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Month = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CreatedByName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    VerifiedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    VerifiedByName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    VerifiedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ApprovedByName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    ApprovedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TotalEmployees = table.Column<int>(type: "int", nullable: false),
                    TotalEligibleEmployees = table.Column<int>(type: "int", nullable: false),
                    TotalBonus = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    IsInactive = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollBonusRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollBonusRuns_PayrollBenefitRules_BenefitRuleId",
                        column: x => x.BenefitRuleId,
                        principalTable: "PayrollBenefitRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollBonusRuns_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PayrollBonusLines",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    BonusRunId = table.Column<long>(type: "bigint", nullable: false),
                    PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StaffId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EmployeeNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Designation = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Department = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    DateOfJoining = table.Column<DateOnly>(type: "date", nullable: true),
                    Scale = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    ValidationMessage = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    BaseSalary = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    BonusAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    BasicBonus = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    AttendanceBonus = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    LeaveBonus = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DisciplineBonus = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    AssessmentBonus = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ServiceBonus = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ServiceYears = table.Column<decimal>(type: "decimal(9,2)", nullable: false),
                    Month = table.Column<int>(type: "int", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    TotalBonus = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    BasicPercent = table.Column<decimal>(type: "decimal(9,4)", nullable: false),
                    ServicePercent = table.Column<decimal>(type: "decimal(9,4)", nullable: false),
                    AttendancePercent = table.Column<decimal>(type: "decimal(9,4)", nullable: false),
                    AssessmentPercent = table.Column<decimal>(type: "decimal(9,4)", nullable: false),
                    LeavePercent = table.Column<decimal>(type: "decimal(9,4)", nullable: false),
                    DisciplinePercent = table.Column<decimal>(type: "decimal(9,4)", nullable: false),
                    InstallmentAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Installment = table.Column<int>(type: "int", nullable: false),
                    IsApproved = table.Column<bool>(type: "bit", nullable: false),
                    IsPaid = table.Column<bool>(type: "bit", nullable: false),
                    PaidOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsInactive = table.Column<bool>(type: "bit", nullable: false),
                    Remarks = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollBonusLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollBonusLines_PayrollBonusRuns_BonusRunId",
                        column: x => x.BonusRunId,
                        principalTable: "PayrollBonusRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PayrollBonusLines_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollBonusLines_BonusRunId",
                table: "PayrollBonusLines",
                column: "BonusRunId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollBonusLines_TenantId_BonusRunId_PersonId",
                table: "PayrollBonusLines",
                columns: new[] { "TenantId", "BonusRunId", "PersonId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollBonusLines_TenantId_PersonId_IsPaid",
                table: "PayrollBonusLines",
                columns: new[] { "TenantId", "PersonId", "IsPaid" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollBonusRuns_BenefitRuleId",
                table: "PayrollBonusRuns",
                column: "BenefitRuleId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollBonusRuns_TenantId_BenefitRuleId_Year_Month",
                table: "PayrollBonusRuns",
                columns: new[] { "TenantId", "BenefitRuleId", "Year", "Month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollBonusRuns_TenantId_RunNumber",
                table: "PayrollBonusRuns",
                columns: new[] { "TenantId", "RunNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollBonusRuns_TenantId_Status_Year_Month",
                table: "PayrollBonusRuns",
                columns: new[] { "TenantId", "Status", "Year", "Month" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PayrollBonusLines");

            migrationBuilder.DropTable(
                name: "PayrollBonusRuns");
        }
    }
}
