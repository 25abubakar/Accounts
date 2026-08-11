using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260811121500_LinkJobTitlesToDesignations")]
public sealed class LinkJobTitlesToDesignations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF COL_LENGTH(N'dbo.JobTitles', N'PlatformTypeValueId') IS NULL
                ALTER TABLE dbo.JobTitles ADD PlatformTypeValueId int NULL;
            """);

        // SQL Server compiles a complete command before executing it. Keep the
        // column creation in its own command so later statements can resolve it.
        migrationBuilder.Sql("""
            -- Remove the obsolete pre-multitenancy uniqueness rule. Different
            -- tenants may legitimately use the same designation name.
            IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.JobTitles') AND name=N'UIX_JobTitles_TitleName')
                ALTER TABLE dbo.JobTitles DROP CONSTRAINT UIX_JobTitles_TitleName;

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.JobTitles') AND name=N'IX_JobTitles_TenantId_TitleName')
                CREATE UNIQUE INDEX IX_JobTitles_TenantId_TitleName ON dbo.JobTitles(TenantId,TitleName);

            DECLARE @DesignationCategoryId int =
                (SELECT TOP(1) Id FROM dbo.PlatformTypeCategories WHERE Code=N'DESIGNATION' AND IsActive=1 ORDER BY Id);
            IF @DesignationCategoryId IS NULL
                THROW 51040, 'The Designation platform type category is not configured.', 1;

            -- Link every existing JobTitle to an existing same-name designation first.
            UPDATE title
               SET PlatformTypeValueId=designation.Id
            FROM dbo.JobTitles title
            CROSS APPLY (
                SELECT TOP(1) value.Id
                FROM dbo.PlatformTypeValues value
                WHERE value.TenantId=title.TenantId
                  AND value.CategoryId=@DesignationCategoryId
                  AND UPPER(LTRIM(RTRIM(value.Name)))=UPPER(LTRIM(RTRIM(title.TitleName)))
                ORDER BY value.Id
            ) designation
            WHERE title.PlatformTypeValueId IS NULL;

            -- Preserve titles that were not yet entered on the new Types screen.
            INSERT dbo.PlatformTypeValues
                (TenantId,CategoryId,Name,Code,DisplayOrder,IsActive,CreatedOnUtc)
            SELECT title.TenantId,@DesignationCategoryId,title.TitleName,
                   CONCAT(N'DESIGNATION_',title.Id),0,1,SYSUTCDATETIME()
            FROM dbo.JobTitles title
            WHERE title.PlatformTypeValueId IS NULL;

            UPDATE title
               SET PlatformTypeValueId=value.Id
            FROM dbo.JobTitles title
            JOIN dbo.PlatformTypeValues value
              ON value.TenantId=title.TenantId
             AND value.CategoryId=@DesignationCategoryId
             AND value.Code=CONCAT(N'DESIGNATION_',title.Id)
            WHERE title.PlatformTypeValueId IS NULL;

            -- Designations created in Types also become available to vacancies.
            INSERT dbo.JobTitles (TenantId,TitleName,AttendanceVisibilityScope,PlatformTypeValueId)
            SELECT value.TenantId,value.Name,0,value.Id
            FROM dbo.PlatformTypeValues value
            WHERE value.CategoryId=@DesignationCategoryId
              AND NOT EXISTS (SELECT 1 FROM dbo.JobTitles title WHERE title.PlatformTypeValueId=value.Id);

            ALTER TABLE dbo.JobTitles ALTER COLUMN PlatformTypeValueId int NOT NULL;

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.JobTitles') AND name=N'IX_JobTitles_PlatformTypeValueId')
                CREATE UNIQUE INDEX IX_JobTitles_PlatformTypeValueId ON dbo.JobTitles(PlatformTypeValueId);

            IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_JobTitles_PlatformTypeValues_PlatformTypeValueId')
                ALTER TABLE dbo.JobTitles WITH CHECK ADD CONSTRAINT FK_JobTitles_PlatformTypeValues_PlatformTypeValueId
                    FOREIGN KEY(PlatformTypeValueId) REFERENCES dbo.PlatformTypeValues(Id);

            -- Designation is now the single Platform Settings master screen.
            UPDATE dbo.Menus SET IsActive=0 WHERE Route=N'/settings/job-titles';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_JobTitles_PlatformTypeValues_PlatformTypeValueId')
                ALTER TABLE dbo.JobTitles DROP CONSTRAINT FK_JobTitles_PlatformTypeValues_PlatformTypeValueId;
            IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.JobTitles') AND name=N'IX_JobTitles_PlatformTypeValueId')
                DROP INDEX IX_JobTitles_PlatformTypeValueId ON dbo.JobTitles;
            IF COL_LENGTH(N'dbo.JobTitles', N'PlatformTypeValueId') IS NOT NULL
                ALTER TABLE dbo.JobTitles DROP COLUMN PlatformTypeValueId;
            UPDATE dbo.Menus SET IsActive=1 WHERE Route=N'/settings/job-titles';
            """);
    }
}
