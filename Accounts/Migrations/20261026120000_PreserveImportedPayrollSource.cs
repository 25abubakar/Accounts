using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261026120000_PreserveImportedPayrollSource")]
public sealed class PreserveImportedPayrollSource : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "SourceSnapshotJson", table: "PayrollLines", type: "nvarchar(max)", nullable: true);
        migrationBuilder.AddColumn<bool>(name: "UseSourceSnapshot", table: "PayrollLines", type: "bit", nullable: false, defaultValue: false);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("IF EXISTS(SELECT 1 FROM dbo.PayrollLines WHERE SourceSnapshotJson IS NOT NULL) THROW 51000, 'Export imported payroll sources before removing their storage.', 1;");
        migrationBuilder.DropColumn(name: "SourceSnapshotJson", table: "PayrollLines");
        migrationBuilder.DropColumn(name: "UseSourceSnapshot", table: "PayrollLines");
    }
}
