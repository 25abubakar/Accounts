using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260813163000_SyncPlatformSettingsFromMasterTenant")]
public sealed class SyncPlatformSettingsFromMasterTenant : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DECLARE @SourceTenantId int =
            (
                SELECT TOP (1) tenant.Id
                FROM dbo.Tenants tenant
                WHERE tenant.IsActive = 1
                  AND UPPER(LTRIM(RTRIM(tenant.TenantName))) IN (N'LAL TECHNOLOGIES', N'LAL GROUP OF TECHNOLOGIES')
                ORDER BY CASE WHEN UPPER(LTRIM(RTRIM(tenant.TenantName))) = N'LAL TECHNOLOGIES' THEN 0 ELSE 1 END,
                         tenant.Id
            );

            IF @SourceTenantId IS NULL
                SET @SourceTenantId =
                (
                    SELECT TOP (1) title.TenantId
                    FROM dbo.JobTitles title
                    GROUP BY title.TenantId
                    ORDER BY COUNT(*) DESC, title.TenantId
                );

            IF @SourceTenantId IS NULL
                RETURN;

            -- Copy designations (JobTitles) to every tenant that is missing them.
            INSERT INTO dbo.JobTitles (TenantId, TitleName, AttendanceVisibilityScope)
            SELECT target.Id, source.TitleName, source.AttendanceVisibilityScope
            FROM dbo.Tenants target
            CROSS JOIN dbo.JobTitles source
            WHERE source.TenantId = @SourceTenantId
              AND target.IsActive = 1
              AND NOT EXISTS (
                    SELECT 1
                    FROM dbo.JobTitles existing
                    WHERE existing.TenantId = target.Id
                      AND UPPER(LTRIM(RTRIM(existing.TitleName))) = UPPER(LTRIM(RTRIM(source.TitleName)))
              );

            -- Also map any legacy PlatformTypeValues DESIGNATION rows into JobTitles.
            DECLARE @DesignationCategoryId int =
            (
                SELECT TOP (1) category.Id
                FROM dbo.PlatformTypeCategories category
                WHERE category.Code = N'DESIGNATION' AND category.IsActive = 1
                ORDER BY category.Id
            );

            IF @DesignationCategoryId IS NOT NULL
            BEGIN
                INSERT INTO dbo.JobTitles (TenantId, TitleName, AttendanceVisibilityScope)
                SELECT value.TenantId, value.Name, 0
                FROM dbo.PlatformTypeValues value
                WHERE value.CategoryId = @DesignationCategoryId
                  AND value.IsActive = 1
                  AND NOT EXISTS (
                        SELECT 1
                        FROM dbo.JobTitles existing
                        WHERE existing.TenantId = value.TenantId
                          AND UPPER(LTRIM(RTRIM(existing.TitleName))) = UPPER(LTRIM(RTRIM(value.Name)))
                  );
            END

            DECLARE @tables TABLE (TableName sysname NOT NULL PRIMARY KEY);
            INSERT @tables (TableName) VALUES
              (N'ContractTypes'),(N'FrequencyTypes'),(N'RateTypes'),(N'AllowanceTypes'),
              (N'TadaTypes'),(N'LeaveTypes'),(N'AnnouncementTypes'),(N'AssessmentTypes'),
              (N'AttendanceTypes'),(N'BenefitTypes');

            DECLARE @table sysname, @sql nvarchar(max);
            DECLARE table_cursor CURSOR LOCAL FAST_FORWARD FOR SELECT TableName FROM @tables;
            OPEN table_cursor;
            FETCH NEXT FROM table_cursor INTO @table;
            WHILE @@FETCH_STATUS = 0
            BEGIN
                IF OBJECT_ID(N'PlatformTypes.' + @table, N'U') IS NOT NULL
                BEGIN
                    SET @sql = N'
                        INSERT PlatformTypes.' + QUOTENAME(@table) + N'
                            (TenantId,Name,Code,DisplayOrder,IsActive,CreatedOnUtc)
                        SELECT target.Id, source.Name, source.Code, source.DisplayOrder, source.IsActive, SYSUTCDATETIME()
                        FROM dbo.Tenants target
                        CROSS JOIN PlatformTypes.' + QUOTENAME(@table) + N' source
                        WHERE source.TenantId = @SourceTenantId
                          AND target.IsActive = 1
                          AND NOT EXISTS (
                                SELECT 1
                                FROM PlatformTypes.' + QUOTENAME(@table) + N' existing
                                WHERE existing.TenantId = target.Id
                                  AND existing.Code = source.Code
                          );';
                    EXEC sys.sp_executesql @sql, N'@SourceTenantId int', @SourceTenantId = @SourceTenantId;
                END
                FETCH NEXT FROM table_cursor INTO @table;
            END
            CLOSE table_cursor;
            DEALLOCATE table_cursor;

            EXEC(N'CREATE OR ALTER VIEW PlatformTypes.Designations AS
                SELECT Id, TenantId, TitleName AS Name, AttendanceVisibilityScope
                FROM dbo.JobTitles');
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Data sync is intentionally not rolled back.
    }
}
