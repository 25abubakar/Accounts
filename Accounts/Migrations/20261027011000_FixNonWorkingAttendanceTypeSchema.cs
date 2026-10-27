using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261027011000_FixNonWorkingAttendanceTypeSchema")]
public sealed class FixNonWorkingAttendanceTypeSchema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        using var stream=typeof(FixNonWorkingAttendanceTypeSchema).Assembly.GetManifestResourceStream("Accounts.Migrations.Sql.20261027010000_EnsureNonWorkingAttendance.sql")!;
        using var reader=new StreamReader(stream);
        migrationBuilder.Sql(reader.ReadToEnd());
    }
    protected override void Down(MigrationBuilder migrationBuilder) { }
}
