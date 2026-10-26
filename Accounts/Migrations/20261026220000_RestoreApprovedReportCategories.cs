using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261026220000_RestoreApprovedReportCategories")]
public sealed class RestoreApprovedReportCategories : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        -- Restore only the original shared vocabulary from AddProcessReportWorkflow.
        -- Do not change existing categories, tenant customizations or approver assignments.
        INSERT INTO dbo.ProcessWorkflowCategories (Code, Name, IsActive, DisplayOrder)
        SELECT source.Code, source.Name, 1, source.DisplayOrder
        FROM (VALUES
            (N'ATTENDANCE', N'Attendance', 10),
            (N'LEAVE', N'Leave', 20),
            (N'PAYROLL', N'Payroll', 30),
            (N'HR', N'Human Resources', 40),
            (N'GENERAL', N'General', 50)
        ) source(Code, Name, DisplayOrder)
        WHERE NOT EXISTS (SELECT 1 FROM dbo.ProcessWorkflowCategories existing WHERE existing.Code=source.Code);
        """);

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Categories can already be referenced by reports/approvers; retain configuration.
    }
}
