using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260811134500_OrganizeTenantPlatformTypeMasters")]
public sealed class OrganizeTenantPlatformTypeMasters : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF SCHEMA_ID(N'PlatformTypes') IS NULL
                EXEC(N'CREATE SCHEMA PlatformTypes AUTHORIZATION dbo');

            DECLARE @tables TABLE (TableName sysname NOT NULL PRIMARY KEY);
            INSERT @tables (TableName) VALUES
              (N'ContractTypes'),
              (N'FrequencyTypes'),
              (N'RateTypes'),
              (N'AllowanceTypes'),
              (N'TadaTypes'),
              (N'LeaveTypes'),
              (N'AnnouncementTypes'),
              (N'AssessmentTypes'),
              (N'AttendanceTypes'),
              (N'BenefitTypes');

            DECLARE @table sysname, @sql nvarchar(max);
            DECLARE table_cursor CURSOR LOCAL FAST_FORWARD FOR
                SELECT TableName FROM @tables;
            OPEN table_cursor;
            FETCH NEXT FROM table_cursor INTO @table;
            WHILE @@FETCH_STATUS=0
            BEGIN
                -- Earlier seed migrations copied defaults to every tenant.
                -- Defaults belong only to the two original Lal companies;
                -- every other/new company starts empty and creates its own.
                IF OBJECT_ID(N'dbo.' + @table, N'U') IS NOT NULL
                BEGIN
                    SET @sql=N'DELETE value
                        FROM dbo.' + QUOTENAME(@table) + N' value
                        JOIN dbo.Tenants tenant ON tenant.Id=value.TenantId
                        WHERE UPPER(LTRIM(RTRIM(tenant.TenantName))) NOT IN
                              (N''LAL TECHNOLOGIES'',N''LAL GROUP OF TECHNOLOGIES'');';
                    EXEC sys.sp_executesql @sql;

                    IF OBJECT_ID(N'PlatformTypes.' + @table, N'U') IS NULL
                    BEGIN
                        SET @sql=N'ALTER SCHEMA PlatformTypes TRANSFER dbo.' + QUOTENAME(@table) + N';';
                        EXEC sys.sp_executesql @sql;
                    END
                END

                FETCH NEXT FROM table_cursor INTO @table;
            END
            CLOSE table_cursor;
            DEALLOCATE table_cursor;

            -- Designation remains backed by JobTitles because vacancies and
            -- attendance already reference that canonical table. Remove only
            -- copied, unused titles from other companies; never delete a title
            -- that an existing vacancy uses.
            DELETE title
            FROM dbo.JobTitles title
            JOIN dbo.Tenants tenant ON tenant.Id=title.TenantId
            WHERE UPPER(LTRIM(RTRIM(tenant.TenantName))) NOT IN
                  (N'LAL TECHNOLOGIES',N'LAL GROUP OF TECHNOLOGIES')
              AND NOT EXISTS
                  (SELECT 1 FROM dbo.Vacancies vacancy WHERE vacancy.JobTitleId=title.Id);

            -- A read-only, clearly named database projection makes Designation
            -- discoverable beside the other masters while preserving every
            -- existing Vacancy -> JobTitles foreign key and stored procedure.
            EXEC(N'CREATE OR ALTER VIEW PlatformTypes.Designations AS
                SELECT Id,TenantId,TitleName AS Name,AttendanceVisibilityScope
                FROM dbo.JobTitles');
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF OBJECT_ID(N'PlatformTypes.Designations',N'V') IS NOT NULL
                DROP VIEW PlatformTypes.Designations;

            DECLARE @tables TABLE (TableName sysname NOT NULL PRIMARY KEY);
            INSERT @tables (TableName) VALUES
              (N'ContractTypes'),(N'FrequencyTypes'),(N'RateTypes'),
              (N'AllowanceTypes'),(N'TadaTypes'),(N'LeaveTypes'),
              (N'AnnouncementTypes'),(N'AssessmentTypes'),
              (N'AttendanceTypes'),(N'BenefitTypes');

            DECLARE @table sysname, @sql nvarchar(max);
            DECLARE table_cursor CURSOR LOCAL FAST_FORWARD FOR SELECT TableName FROM @tables;
            OPEN table_cursor;
            FETCH NEXT FROM table_cursor INTO @table;
            WHILE @@FETCH_STATUS=0
            BEGIN
                IF OBJECT_ID(N'PlatformTypes.' + @table,N'U') IS NOT NULL
                   AND OBJECT_ID(N'dbo.' + @table,N'U') IS NULL
                BEGIN
                    SET @sql=N'ALTER SCHEMA dbo TRANSFER PlatformTypes.' + QUOTENAME(@table) + N';';
                    EXEC sys.sp_executesql @sql;
                END
                FETCH NEXT FROM table_cursor INTO @table;
            END
            CLOSE table_cursor;
            DEALLOCATE table_cursor;

            -- Removed copied seed rows are intentionally not recreated.
            """);
    }
}
