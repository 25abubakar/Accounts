using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260720220000_AddAttendanceHolidayColorMaps")]
public sealed class AddAttendanceHolidayColorMaps : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AttendanceHolidayColorMaps",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                TenantId = table.Column<int>(type: "int", nullable: false),
                HolidayTypeCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                ColorCode = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: false),
                CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                ModifiedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
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

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "AttendanceHolidayColorMaps");
}
