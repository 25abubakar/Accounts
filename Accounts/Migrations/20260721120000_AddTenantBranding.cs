using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260721120000_AddTenantBranding")]
public partial class AddTenantBranding : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF COL_LENGTH('dbo.Tenants', 'BrandingFileName') IS NULL
                ALTER TABLE [dbo].[Tenants] ADD [BrandingFileName] nvarchar(255) NULL;
            IF COL_LENGTH('dbo.Tenants', 'BrandingContentType') IS NULL
                ALTER TABLE [dbo].[Tenants] ADD [BrandingContentType] nvarchar(100) NULL;
            IF COL_LENGTH('dbo.Tenants', 'BrandingAssetType') IS NULL
                ALTER TABLE [dbo].[Tenants] ADD [BrandingAssetType] nvarchar(20) NULL;
            IF COL_LENGTH('dbo.Tenants', 'BrandingContent') IS NULL
                ALTER TABLE [dbo].[Tenants] ADD [BrandingContent] varbinary(max) NULL;
            IF COL_LENGTH('dbo.Tenants', 'BrandingUpdatedOnUtc') IS NULL
                ALTER TABLE [dbo].[Tenants] ADD [BrandingUpdatedOnUtc] datetime2 NULL;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF COL_LENGTH('dbo.Tenants', 'BrandingUpdatedOnUtc') IS NOT NULL ALTER TABLE [dbo].[Tenants] DROP COLUMN [BrandingUpdatedOnUtc];
            IF COL_LENGTH('dbo.Tenants', 'BrandingContent') IS NOT NULL ALTER TABLE [dbo].[Tenants] DROP COLUMN [BrandingContent];
            IF COL_LENGTH('dbo.Tenants', 'BrandingAssetType') IS NOT NULL ALTER TABLE [dbo].[Tenants] DROP COLUMN [BrandingAssetType];
            IF COL_LENGTH('dbo.Tenants', 'BrandingContentType') IS NOT NULL ALTER TABLE [dbo].[Tenants] DROP COLUMN [BrandingContentType];
            IF COL_LENGTH('dbo.Tenants', 'BrandingFileName') IS NOT NULL ALTER TABLE [dbo].[Tenants] DROP COLUMN [BrandingFileName];
            """);
    }
}
