using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

/// <summary>
/// Ensure Deduction process category exists for Approve Process + PIN mapping.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260911120000_EnsureDeductionProcessCategory")]
public sealed class EnsureDeductionProcessCategory : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF NOT EXISTS (SELECT 1 FROM dbo.ProcessWorkflowCategories WHERE Code = N'DEDUCTION')
            BEGIN
                INSERT INTO dbo.ProcessWorkflowCategories (Code, Name, IsActive, DisplayOrder)
                VALUES (N'DEDUCTION', N'Deduction', 1, 35);
            END
            ELSE
            BEGIN
                UPDATE dbo.ProcessWorkflowCategories
                SET Name = N'Deduction', IsActive = 1
                WHERE Code = N'DEDUCTION';
            END
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
