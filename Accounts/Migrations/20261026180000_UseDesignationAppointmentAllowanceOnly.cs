using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Text.RegularExpressions;

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261026180000_UseDesignationAppointmentAllowanceOnly")]
public sealed class UseDesignationAppointmentAllowanceOnly : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        using var stream=typeof(UseDesignationAppointmentAllowanceOnly).Assembly.GetManifestResourceStream(
            "Accounts.Migrations.Sql.20261026180000_GenerateMonthly.sql")!;
        using var reader=new StreamReader(stream);
        foreach(var batch in Regex.Split(reader.ReadToEnd(),@"^\s*GO\s*$",RegexOptions.Multiline|RegexOptions.IgnoreCase))
            if(!string.IsNullOrWhiteSpace(batch))migrationBuilder.Sql(batch);
    }
    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Restore the reviewed previous payroll generation procedure before rollback.");
}
