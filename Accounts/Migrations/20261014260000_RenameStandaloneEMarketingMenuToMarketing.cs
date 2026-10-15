using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

/// <summary>
/// Renames only the standalone parent that owns Stock Info and Sales (Roz).
/// The Sale Roznamcha child named E-Marketing is intentionally unchanged.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20261014260000_RenameStandaloneEMarketingMenuToMarketing")]
public sealed class RenameStandaloneEMarketingMenuToMarketing : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE parent
            SET parent.Title = N'Marketing'
            FROM dbo.Menus parent
            WHERE parent.ParentId IS NULL
              AND parent.Title IN (N'E-Marketing', N'E_Marketing', N'E Marketing')
              AND EXISTS
              (
                  SELECT 1
                  FROM dbo.Menus child
                  WHERE child.ParentId = parent.Id
                    AND child.Route IN (N'/e-marketing/stock-info', N'/e-marketing/sales-roz')
              );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE parent
            SET parent.Title = N'E-Marketing'
            FROM dbo.Menus parent
            WHERE parent.ParentId IS NULL
              AND parent.Title = N'Marketing'
              AND EXISTS
              (
                  SELECT 1
                  FROM dbo.Menus child
                  WHERE child.ParentId = parent.Id
                    AND child.Route IN (N'/e-marketing/stock-info', N'/e-marketing/sales-roz')
              );
            """);
    }
}
