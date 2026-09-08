using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

/// <summary>
/// Placeholder: dynamic CREATE OR ALTER via OBJECT_DEFINITION breaks MigrateAsync
/// ("CREATE/ALTER PROCEDURE must be the first statement in a query batch").
/// Duplicate AttendanceRecords inserts are handled in AttendanceService.EvaluateStatusesAsync
/// and the idempotent INSERT pattern in alter_sp2.sql for manual/ops apply.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260908120000_FixEvaluateStatusesDuplicateAttendanceInsert")]
public sealed class FixEvaluateStatusesDuplicateAttendanceInsert : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Intentionally empty — do not rewrite usp_Attendance_EvaluateStatuses here.
        // Runtime duplicate-key handling covers the crash; apply alter_sp2.sql when convenient.
        migrationBuilder.Sql("SELECT 1;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
