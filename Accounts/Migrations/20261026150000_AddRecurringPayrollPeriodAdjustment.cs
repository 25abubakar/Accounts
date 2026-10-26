using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Text.RegularExpressions;

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261026150000_AddRecurringPayrollPeriodAdjustment")]
public sealed class AddRecurringPayrollPeriodAdjustment : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        foreach (var table in new[] { "PersonHrProfiles", "PayrollLines" })
        {
            migrationBuilder.AddColumn<decimal>("PeriodAdjustmentAmount", table, type: "decimal(18,2)", nullable: true);
            migrationBuilder.AddColumn<string>("PeriodAdjustmentRemarks", table, type: "nvarchar(1000)", maxLength: 1000, nullable: true);
        }
        foreach (var name in new[] { "GenerateMonthly", "RecalculateRunTotals" })
        {
            using var stream = typeof(AddRecurringPayrollPeriodAdjustment).Assembly.GetManifestResourceStream(
                $"Accounts.Migrations.Sql.20261026150000_{name}.sql")!;
            using var reader = new StreamReader(stream);
            foreach (var batch in Regex.Split(reader.ReadToEnd(), @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
                if (!string.IsNullOrWhiteSpace(batch)) migrationBuilder.Sql(batch);
        }
    }
    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Export recurring payroll adjustments and restore reviewed procedures before rollback.");
}
