using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260714190000_AddAppNoteBanner")]
    public sealed class AddAppNoteBanner : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF COL_LENGTH('dbo.AppNotes', 'IsBanner') IS NULL
BEGIN
    ALTER TABLE dbo.AppNotes ADD IsBanner bit NOT NULL
        CONSTRAINT DF_AppNotes_IsBanner DEFAULT (0);
END;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF COL_LENGTH('dbo.AppNotes', 'IsBanner') IS NOT NULL
    ALTER TABLE dbo.AppNotes DROP CONSTRAINT IF EXISTS DF_AppNotes_IsBanner;
IF COL_LENGTH('dbo.AppNotes', 'IsBanner') IS NOT NULL
    ALTER TABLE dbo.AppNotes DROP COLUMN IsBanner;");
        }
    }
}
