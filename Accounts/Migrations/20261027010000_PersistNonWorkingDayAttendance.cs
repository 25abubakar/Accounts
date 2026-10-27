using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261027010000_PersistNonWorkingDayAttendance")]
public sealed class PersistNonWorkingDayAttendance : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>("IsSystemGeneratedNonWorkingDay","AttendanceRecords",type:"bit",nullable:false,defaultValue:false);
        using var stream=typeof(PersistNonWorkingDayAttendance).Assembly.GetManifestResourceStream("Accounts.Migrations.Sql.20261027010000_EnsureNonWorkingAttendance.sql")!;
        using var reader=new StreamReader(stream);
        migrationBuilder.Sql(reader.ReadToEnd());
    }
    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Preserve generated attendance IDs and provenance before rolling back this migration.");
}
