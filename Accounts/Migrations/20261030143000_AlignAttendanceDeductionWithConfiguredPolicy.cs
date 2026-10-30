using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Text.RegularExpressions;

#nullable disable

namespace Accounts.Migrations;

/// <summary>
/// Keeps the deduction report and payroll consumer on one calculation contract:
/// configured fixed rates, discrete whole-hour charging, one absent-day waiver,
/// and the attendance rule's Friday/Monday weekend multiplier.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20261030143000_AlignAttendanceDeductionWithConfiguredPolicy")]
public sealed class AlignAttendanceDeductionWithConfiguredPolicy : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ApplySql(migrationBuilder,
            "Accounts.Sql.StoredProcedures.usp_Attendance_DeductionReport.sql");
        ApplySql(migrationBuilder,
            "Accounts.Sql.StoredProcedures.usp_Payroll_GenerateMonthly.sql");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Procedure-only correction. Retaining the corrected calculation on rollback
        // is safer than silently restoring the known double-relief payroll defect.
    }

    private static void ApplySql(MigrationBuilder migrationBuilder, string resourceName)
    {
        var assembly = typeof(AlignAttendanceDeductionWithConfiguredPolicy).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded SQL resource '{resourceName}' was not found.");
        using var reader = new StreamReader(stream);
        foreach (var batch in Regex.Split(
                     reader.ReadToEnd(),
                     @"^\s*GO\s*$",
                     RegexOptions.Multiline | RegexOptions.IgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(batch))
                migrationBuilder.Sql(batch);
        }
    }
}
