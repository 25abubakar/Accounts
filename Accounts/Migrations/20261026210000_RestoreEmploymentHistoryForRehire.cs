using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261026210000_RestoreEmploymentHistoryForRehire")]
public sealed class RestoreEmploymentHistoryForRehire : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Existing historical table is retained. Imported histories may have unknown dates/staff keys.
        migrationBuilder.AlterColumn<Guid>("StaffId","PersonEmploymentHistory",type:"uniqueidentifier",nullable:true,oldClrType:typeof(Guid),oldType:"uniqueidentifier");
        migrationBuilder.AlterColumn<DateTime>("EndingDateUtc","PersonEmploymentHistory",type:"datetime2",nullable:true,oldClrType:typeof(DateTime),oldType:"datetime2");
    }
    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Preserve unknown historical dates and staff identifiers before rolling back employment history.");
}
