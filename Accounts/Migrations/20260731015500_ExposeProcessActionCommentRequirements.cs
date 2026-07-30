using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260731015500_ExposeProcessActionCommentRequirements")]
public sealed class ExposeProcessActionCommentRequirements : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE OR ALTER PROCEDURE dbo.usp_ProcessReport_Lookups
            AS
            BEGIN
                SET NOCOUNT ON;

                SELECT N'CATEGORY' AS LookupType, Code, Name,
                       CAST(NULL AS nvarchar(20)) AS ColorCode, DisplayOrder,
                       CAST(0 AS bit) AS RequiresComments
                FROM dbo.ProcessWorkflowCategories
                WHERE IsActive = 1

                UNION ALL

                SELECT N'PRIORITY', Code, Name, ColorCode, DisplayOrder,
                       CAST(0 AS bit)
                FROM dbo.ProcessWorkflowPriorities
                WHERE IsActive = 1

                UNION ALL

                SELECT N'ACTION', Code, Name, ColorCode, DisplayOrder,
                       RequiresComments
                FROM dbo.ProcessWorkflowActionTypes
                WHERE IsActive = 1 AND Code <> N'SUBMIT'

                ORDER BY LookupType, DisplayOrder;
            END
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE OR ALTER PROCEDURE dbo.usp_ProcessReport_Lookups
            AS
            BEGIN
                SET NOCOUNT ON;
                SELECT N'CATEGORY' AS LookupType,Code,Name,
                       CAST(NULL AS nvarchar(20)) AS ColorCode,DisplayOrder
                FROM dbo.ProcessWorkflowCategories WHERE IsActive=1
                UNION ALL
                SELECT N'PRIORITY',Code,Name,ColorCode,DisplayOrder
                FROM dbo.ProcessWorkflowPriorities WHERE IsActive=1
                UNION ALL
                SELECT N'ACTION',Code,Name,ColorCode,DisplayOrder
                FROM dbo.ProcessWorkflowActionTypes WHERE IsActive=1 AND Code<>N'SUBMIT'
                ORDER BY LookupType,DisplayOrder;
            END
            """);
    }
}
