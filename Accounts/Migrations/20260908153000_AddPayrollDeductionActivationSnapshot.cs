using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260908153000_AddPayrollDeductionActivationSnapshot")]
public sealed class AddPayrollDeductionActivationSnapshot : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "IsAttendanceDeductionActive",
            table: "PayrollLines",
            type: "bit",
            nullable: false,
            defaultValue: true);

        migrationBuilder.Sql(
            """
            UPDATE payrollLine
            SET payrollLine.IsAttendanceDeductionActive =
                CASE WHEN EXISTS
                (
                    SELECT 1
                    FROM dbo.AttendanceMapRules attendanceMap
                    INNER JOIN dbo.AttendanceRuleSettings attendanceRule
                        ON attendanceRule.TenantId = attendanceMap.TenantId
                       AND attendanceRule.AttendanceEntryTypeId = attendanceMap.AttendanceEntryTypeId
                       AND attendanceRule.IsActive = 1
                       AND attendanceRule.IsApproved = 1
                    WHERE attendanceMap.TenantId = payrollLine.TenantId
                      AND attendanceMap.StaffId = payrollLine.StaffId
                ) THEN 1 ELSE 0 END
            FROM dbo.PayrollLines payrollLine
            INNER JOIN dbo.PayrollRuns payrollRun ON payrollRun.Id = payrollLine.PayrollRunId
            WHERE payrollRun.Status = N'Draft';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "IsAttendanceDeductionActive",
            table: "PayrollLines");
    }
}
