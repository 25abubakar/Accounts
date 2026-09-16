using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260916130000_DepartmentScopedAssessmentPositions")]
public sealed class DepartmentScopedAssessmentPositions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        -- The old unique index scoped a position to one assessor, so it blocked
        -- position 1 in two different departments while allowing primary and
        -- alternative reporters to submit the same employee independently.
        -- The assessment save transaction now checks both invariants across
        -- the tenant's full period before committing.
        IF EXISTS (SELECT 1 FROM sys.indexes
                   WHERE object_id = OBJECT_ID(N'dbo.StaffAssessments')
                     AND name = N'UX_StaffAssessments_UniqueMonthlyRank')
            DROP INDEX UX_StaffAssessments_UniqueMonthlyRank ON dbo.StaffAssessments;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) { }
}
