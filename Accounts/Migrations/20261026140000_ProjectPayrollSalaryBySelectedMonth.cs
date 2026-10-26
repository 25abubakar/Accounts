using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Text.RegularExpressions;

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261026140000_ProjectPayrollSalaryBySelectedMonth")]
public sealed class ProjectPayrollSalaryBySelectedMonth : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        Apply(migrationBuilder, "ScaleProjection");
        Apply(migrationBuilder, "GenerateMonthly");
    }

    private static void Apply(MigrationBuilder builder, string name)
    {
        using var stream = typeof(ProjectPayrollSalaryBySelectedMonth).Assembly.GetManifestResourceStream(
            $"Accounts.Migrations.Sql.20261026140000_{name}.sql")
            ?? throw new InvalidOperationException("Missing payroll migration SQL.");
        using var reader = new StreamReader(stream);
        foreach (var batch in Regex.Split(reader.ReadToEnd(), @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
            if (!string.IsNullOrWhiteSpace(batch)) builder.Sql(batch);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Restore the reviewed prior payroll procedures before rolling back salary projection.");
}
