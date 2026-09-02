using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class ConnectPayrollCalculation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ApprovedByName",
                table: "PayrollRuns",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedByUserId",
                table: "PayrollRuns",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedOnUtc",
                table: "PayrollRuns",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedByName",
                table: "PayrollRuns",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedByUserId",
                table: "PayrollRuns",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerifiedByName",
                table: "PayrollRuns",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerifiedByUserId",
                table: "PayrollRuns",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "VerifiedOnUtc",
                table: "PayrollRuns",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PayrollLines",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    PayrollRunId = table.Column<long>(type: "bigint", nullable: false),
                    PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StaffId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EmployeeNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Designation = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Department = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    DateOfJoining = table.Column<DateOnly>(type: "date", nullable: true),
                    Scale = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    BasicSalary = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    AllowanceAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    EmployerBenefitAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    StaffBenefitDeduction = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    BonusAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    OvertimeAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    AttendanceDeduction = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    AttendanceAdjustment = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TaxAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    EmployeeEobiAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    EmployerEobiAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    OtherDeduction = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    GrossPay = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TotalDeduction = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    NetPay = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    IsApproved = table.Column<bool>(type: "bit", nullable: false),
                    IsPaid = table.Column<bool>(type: "bit", nullable: false),
                    PaidOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Remarks = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollLines_PayrollRuns_PayrollRunId",
                        column: x => x.PayrollRunId,
                        principalTable: "PayrollRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PayrollLines_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollLines_PayrollRunId",
                table: "PayrollLines",
                column: "PayrollRunId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollLines_TenantId_PayrollRunId_PersonId",
                table: "PayrollLines",
                columns: new[] { "TenantId", "PayrollRunId", "PersonId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollLines_TenantId_PersonId_IsPaid",
                table: "PayrollLines",
                columns: new[] { "TenantId", "PersonId", "IsPaid" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PayrollLines");

            migrationBuilder.DropColumn(
                name: "ApprovedByName",
                table: "PayrollRuns");

            migrationBuilder.DropColumn(
                name: "ApprovedByUserId",
                table: "PayrollRuns");

            migrationBuilder.DropColumn(
                name: "ApprovedOnUtc",
                table: "PayrollRuns");

            migrationBuilder.DropColumn(
                name: "CreatedByName",
                table: "PayrollRuns");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "PayrollRuns");

            migrationBuilder.DropColumn(
                name: "VerifiedByName",
                table: "PayrollRuns");

            migrationBuilder.DropColumn(
                name: "VerifiedByUserId",
                table: "PayrollRuns");

            migrationBuilder.DropColumn(
                name: "VerifiedOnUtc",
                table: "PayrollRuns");
        }
    }
}
