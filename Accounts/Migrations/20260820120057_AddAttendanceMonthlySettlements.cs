using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendanceMonthlySettlements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF OBJECT_ID('dbo.AttendanceHolidayColorMaps', 'U') IS NOT NULL DROP TABLE dbo.AttendanceHolidayColorMaps;");

            migrationBuilder.CreateTable(
                name: "AttendanceMonthlySettlements",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SettlementYear = table.Column<int>(type: "int", nullable: false),
                    SettlementMonth = table.Column<int>(type: "int", nullable: false),
                    IsOvertimeApproved = table.Column<bool>(type: "bit", nullable: false),
                    ApprovedByUserId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ApprovedDateUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceMonthlySettlements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttendanceMonthlySettlements_Persons_PersonId",
                        column: x => x.PersonId,
                        principalTable: "Persons",
                        principalColumn: "PersonId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceMonthlySettlements_PersonId",
                table: "AttendanceMonthlySettlements",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceMonthlySettlements_TenantId_PersonId_SettlementYear_SettlementMonth",
                table: "AttendanceMonthlySettlements",
                columns: new[] { "TenantId", "PersonId", "SettlementYear", "SettlementMonth" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AttendanceMonthlySettlements");

            migrationBuilder.CreateTable(
                name: "AttendanceHolidayColorMaps",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    ColorCode = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    HolidayTypeCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ModifiedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceHolidayColorMaps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttendanceHolidayColorMaps_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceHolidayColorMaps_TenantId_HolidayTypeCode",
                table: "AttendanceHolidayColorMaps",
                columns: new[] { "TenantId", "HolidayTypeCode" },
                unique: true);
        }
    }
}
