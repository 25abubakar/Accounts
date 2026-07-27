using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260727152000_AddStatusManagementReadView")]
public sealed class AddStatusManagementReadView : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE OR ALTER VIEW dbo.vw_StatusConfigurationsForManagement
        AS
        WITH Ranked AS
        (
            SELECT
                style.Id,
                process.ProcessName,
                status.StatusName,
                style.Code,
                style.Description,
                color.ColorName,
                color.ColorCode,
                color.FontColor,
                color.FontSize,
                style.DisplayOrder,
                style.IsPaid,
                style.IsActive,
                style.IsSystem,
                style.TenantId,
                ROW_NUMBER() OVER
                (
                    PARTITION BY
                        ISNULL(style.TenantId, -1),
                        UPPER(LTRIM(RTRIM(status.StatusName))),
                        UPPER(LTRIM(RTRIM(style.Code)))
                    ORDER BY style.IsSystem DESC, style.DisplayOrder ASC, style.Id ASC
                ) AS RowNo
            FROM dbo.ProcessStatusStyles style
            INNER JOIN dbo.Processes process ON process.Id = style.ProcessId
            INNER JOIN dbo.Statuses status ON status.Id = style.StatusId
            INNER JOIN dbo.ColorStyles color ON color.Id = style.ColorStyleId
            WHERE style.IsActive = 1
        )
        SELECT
            Id,
            ProcessName,
            StatusName,
            Code,
            Description,
            ColorName,
            ColorCode,
            FontColor,
            FontSize,
            DisplayOrder,
            IsPaid,
            IsActive,
            IsSystem,
            TenantId
        FROM Ranked
        WHERE RowNo = 1;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("DROP VIEW IF EXISTS dbo.vw_StatusConfigurationsForManagement;");
}
