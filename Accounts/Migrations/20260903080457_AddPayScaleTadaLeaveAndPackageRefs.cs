using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class AddPayScaleTadaLeaveAndPackageRefs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AllowanceReference",
                table: "SalaryPackages",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LeaveReference",
                table: "SalaryPackages",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TadaReference",
                table: "SalaryPackages",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PayScaleLeaves",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    LeaveReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    SalaryScaleId = table.Column<int>(type: "int", nullable: false),
                    LeaveTypeId = table.Column<int>(type: "int", nullable: false),
                    ContractType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    FrequencyType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    RateType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    TotalLeave = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    ApplicableType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ApplicableAfter = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    ValueType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Type = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ApplicableValue = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayScaleLeaves", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayScaleLeaves_LeaveTypes_LeaveTypeId",
                        column: x => x.LeaveTypeId,
                        principalSchema: "PlatformTypes",
                        principalTable: "LeaveTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayScaleLeaves_SalaryScales_SalaryScaleId",
                        column: x => x.SalaryScaleId,
                        principalTable: "SalaryScales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayScaleLeaves_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PayScaleTadas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    TadaReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    SalaryScaleId = table.Column<int>(type: "int", nullable: false),
                    TadaTypeId = table.Column<int>(type: "int", nullable: false),
                    ContractType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    FrequencyType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    RateType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    PayValue = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    CalculatedValue = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayScaleTadas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayScaleTadas_SalaryScales_SalaryScaleId",
                        column: x => x.SalaryScaleId,
                        principalTable: "SalaryScales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayScaleTadas_TadaTypes_TadaTypeId",
                        column: x => x.TadaTypeId,
                        principalSchema: "PlatformTypes",
                        principalTable: "TadaTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayScaleTadas_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PayScaleLeaves_LeaveTypeId",
                table: "PayScaleLeaves",
                column: "LeaveTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_PayScaleLeaves_SalaryScaleId",
                table: "PayScaleLeaves",
                column: "SalaryScaleId");

            migrationBuilder.CreateIndex(
                name: "IX_PayScaleLeaves_TenantId_LeaveReference",
                table: "PayScaleLeaves",
                columns: new[] { "TenantId", "LeaveReference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayScaleLeaves_TenantId_SalaryScaleId_LeaveTypeId_Name",
                table: "PayScaleLeaves",
                columns: new[] { "TenantId", "SalaryScaleId", "LeaveTypeId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayScaleTadas_SalaryScaleId",
                table: "PayScaleTadas",
                column: "SalaryScaleId");

            migrationBuilder.CreateIndex(
                name: "IX_PayScaleTadas_TadaTypeId",
                table: "PayScaleTadas",
                column: "TadaTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_PayScaleTadas_TenantId_SalaryScaleId_TadaTypeId_Name",
                table: "PayScaleTadas",
                columns: new[] { "TenantId", "SalaryScaleId", "TadaTypeId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayScaleTadas_TenantId_TadaReference",
                table: "PayScaleTadas",
                columns: new[] { "TenantId", "TadaReference" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PayScaleLeaves");

            migrationBuilder.DropTable(
                name: "PayScaleTadas");

            migrationBuilder.DropColumn(
                name: "AllowanceReference",
                table: "SalaryPackages");

            migrationBuilder.DropColumn(
                name: "LeaveReference",
                table: "SalaryPackages");

            migrationBuilder.DropColumn(
                name: "TadaReference",
                table: "SalaryPackages");
        }
    }
}
