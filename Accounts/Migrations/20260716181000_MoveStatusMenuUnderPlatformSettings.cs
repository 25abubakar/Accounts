using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260716181000_MoveStatusMenuUnderPlatformSettings")]
public sealed class MoveStatusMenuUnderPlatformSettings : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DECLARE @PlatformSettingsId int = (
                SELECT TOP (1) Id
                FROM Menus
                WHERE Title IN (N'Platform Settings', N'Settings')
                  AND ParentId IS NULL
                  AND IsActive = 1
                ORDER BY CASE WHEN Title = N'Platform Settings' THEN 0 ELSE 1 END, Id);

            IF @PlatformSettingsId IS NOT NULL
            BEGIN
                UPDATE Menus
                SET ParentId = @PlatformSettingsId,
                    SortOrder = CASE WHEN SortOrder < 7 THEN 7 ELSE SortOrder END,
                    IsActive = 1
                WHERE Route = N'/settings/statuses';
            END;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Deliberately keep the visible, supported parent placement.
    }
}
