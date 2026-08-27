using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class AddPayScaleAllowances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PayScaleAllowances",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    AllowanceReference = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    SalaryScaleId = table.Column<int>(type: "int", nullable: false),
                    AllowanceTypeId = table.Column<int>(type: "int", nullable: false),
                    ContractType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    FrequencyType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    RateType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    PayType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    PayValue = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    CalculatedValue = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    AllowanceCategory = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayScaleAllowances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayScaleAllowances_AllowanceTypes_AllowanceTypeId",
                        column: x => x.AllowanceTypeId,
                        principalSchema: "PlatformTypes",
                        principalTable: "AllowanceTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayScaleAllowances_SalaryScales_SalaryScaleId",
                        column: x => x.SalaryScaleId,
                        principalTable: "SalaryScales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayScaleAllowances_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PayScaleAllowances_AllowanceTypeId",
                table: "PayScaleAllowances",
                column: "AllowanceTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_PayScaleAllowances_SalaryScaleId",
                table: "PayScaleAllowances",
                column: "SalaryScaleId");

            migrationBuilder.CreateIndex(
                name: "IX_PayScaleAllowances_TenantId_AllowanceCategory_SalaryScaleId_AllowanceTypeId",
                table: "PayScaleAllowances",
                columns: new[] { "TenantId", "AllowanceCategory", "SalaryScaleId", "AllowanceTypeId" });

            migrationBuilder.CreateIndex(
                name: "IX_PayScaleAllowances_TenantId_AllowanceReference",
                table: "PayScaleAllowances",
                columns: new[] { "TenantId", "AllowanceReference" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PayScaleAllowances");
        }
    }
}
