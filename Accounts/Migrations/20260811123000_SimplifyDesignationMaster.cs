using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260811123000_SimplifyDesignationMaster")]
public sealed class SimplifyDesignationMaster : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_JobTitles_PlatformTypeValues_PlatformTypeValueId')
                ALTER TABLE dbo.JobTitles DROP CONSTRAINT FK_JobTitles_PlatformTypeValues_PlatformTypeValueId;

            IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.JobTitles') AND name=N'IX_JobTitles_PlatformTypeValueId')
                DROP INDEX IX_JobTitles_PlatformTypeValueId ON dbo.JobTitles;

            IF COL_LENGTH(N'dbo.JobTitles', N'PlatformTypeValueId') IS NOT NULL
                ALTER TABLE dbo.JobTitles DROP COLUMN PlatformTypeValueId;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF COL_LENGTH(N'dbo.JobTitles', N'PlatformTypeValueId') IS NULL
                ALTER TABLE dbo.JobTitles ADD PlatformTypeValueId int NULL;
            """);
    }
}
