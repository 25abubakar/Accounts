using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260720150000_AddAttendanceMapRules")]
public sealed class AddAttendanceMapRules : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AttendanceMapRules",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                TenantId = table.Column<int>(type: "int", nullable: false),
                StaffId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                AttendanceEntryTypeId = table.Column<int>(type: "int", nullable: false),
                ShiftCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                TimeFrom = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                TimeTo = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                IsOpenAttendance = table.Column<bool>(type: "bit", nullable: false),
                CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                ModifiedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AttendanceMapRules", x => x.Id);
                table.ForeignKey(
                    name: "FK_AttendanceMapRules_AttendanceEntryTypes_AttendanceEntryTypeId",
                    column: x => x.AttendanceEntryTypeId,
                    principalTable: "AttendanceEntryTypes",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_AttendanceMapRules_StaffVacancy_StaffId",
                    column: x => x.StaffId,
                    principalTable: "StaffVacancy",
                    principalColumn: "StaffId",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_AttendanceMapRules_Tenants_TenantId",
                    column: x => x.TenantId,
                    principalTable: "Tenants",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_AttendanceMapRules_AttendanceEntryTypeId",
            table: "AttendanceMapRules",
            column: "AttendanceEntryTypeId");

        migrationBuilder.CreateIndex(
            name: "IX_AttendanceMapRules_TenantId_AttendanceEntryTypeId",
            table: "AttendanceMapRules",
            columns: new[] { "TenantId", "AttendanceEntryTypeId" });

        migrationBuilder.CreateIndex(
            name: "IX_AttendanceMapRules_TenantId_StaffId",
            table: "AttendanceMapRules",
            columns: new[] { "TenantId", "StaffId" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "AttendanceMapRules");
}
