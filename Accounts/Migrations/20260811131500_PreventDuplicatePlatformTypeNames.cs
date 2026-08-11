using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260811131500_PreventDuplicatePlatformTypeNames")]
public sealed class PreventDuplicatePlatformTypeNames : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DECLARE @tables TABLE (TableName sysname NOT NULL);
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

            DECLARE @table sysname, @index sysname, @sql nvarchar(max);
            DECLARE table_cursor CURSOR LOCAL FAST_FORWARD FOR SELECT TableName FROM @tables;
            OPEN table_cursor;
            FETCH NEXT FROM table_cursor INTO @table;
            WHILE @@FETCH_STATUS=0
            BEGIN
                SET @index=N'IX_' + @table + N'_TenantId_Name';
                IF OBJECT_ID(N'dbo.' + @table,N'U') IS NOT NULL
                   AND NOT EXISTS
                   (
                       SELECT 1 FROM sys.indexes
                       WHERE object_id=OBJECT_ID(N'dbo.' + @table) AND name=@index
                   )
                BEGIN
                    SET @sql=N'CREATE UNIQUE INDEX ' + QUOTENAME(@index)
                        + N' ON dbo.' + QUOTENAME(@table) + N'(TenantId,Name);';
                    EXEC sys.sp_executesql @sql;
                END
                FETCH NEXT FROM table_cursor INTO @table;
            END
            CLOSE table_cursor;
            DEALLOCATE table_cursor;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Non-destructive rollback policy: retaining a uniqueness constraint
        // cannot remove tenant data and protects the master tables from
        // accidental duplicate names.
    }
}
