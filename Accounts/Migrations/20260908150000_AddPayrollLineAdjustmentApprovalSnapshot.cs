using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260908150000_AddPayrollLineAdjustmentApprovalSnapshot")]
public sealed class AddPayrollLineAdjustmentApprovalSnapshot : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "AttendanceAdjustmentRemarks",
            table: "PayrollLines",
            type: "nvarchar(255)",
            maxLength: 255,
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "IsAttendanceAdjustmentApproved",
            table: "PayrollLines",
            type: "bit",
            nullable: false,
            defaultValue: false);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "AttendanceAdjustmentRemarks",
            table: "PayrollLines");

        migrationBuilder.DropColumn(
            name: "IsAttendanceAdjustmentApproved",
            table: "PayrollLines");
    }
}
