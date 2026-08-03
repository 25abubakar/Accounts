using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260731131000_AlignCategoryApprovalTimeline")]
public sealed class AlignCategoryApprovalTimeline : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(FinalizeReportsAtCategoryApprover.TimelineProcedureSql);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Forward-only audit-label correction.
    }
}
