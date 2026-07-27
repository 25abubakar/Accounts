using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260727143000_CleanStatusManagementDuplicates")]
public sealed class CleanStatusManagementDuplicates : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ;WITH Ranked AS
        (
            SELECT
                Id,
                ROW_NUMBER() OVER
                (
                    PARTITION BY ISNULL(TenantId, -1), ProcessId, UPPER(LTRIM(RTRIM(Code)))
                    ORDER BY IsSystem DESC, IsActive DESC, DisplayOrder ASC, Id ASC
                ) AS RowNo
            FROM dbo.ProcessStatusStyles
            WHERE IsActive = 1
        )
        UPDATE style
            SET IsActive = 0,
                ModifiedDate = SYSUTCDATETIME()
        FROM dbo.ProcessStatusStyles style
        JOIN Ranked ranked ON ranked.Id = style.Id
        WHERE ranked.RowNo > 1;
        """);

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Historical duplicate cleanup is intentionally not reversed.
    }
}
