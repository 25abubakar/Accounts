using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260930143100_NormalizeAuditDefaultsToUtc")]
public sealed class NormalizeAuditDefaultsToUtc : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DECLARE @Targets TABLE (TableName sysname, ColumnName sysname);
            INSERT @Targets(TableName, ColumnName) VALUES
                (N'Vacancies', N'CreatedDate'),
                (N'Persons', N'CreatedDate'),
                (N'Features', N'CreatedDate'),
                (N'StaffAccessGroups', N'AssignedDate'),
                (N'DepartmentAccessMatrix', N'GrantedDate'),
                (N'RolePermissions', N'CreatedDate');

            DECLARE @TableName sysname, @ColumnName sysname, @ConstraintName sysname, @Sql nvarchar(max);
            DECLARE target_cursor CURSOR LOCAL FAST_FORWARD FOR
                SELECT TableName, ColumnName FROM @Targets;
            OPEN target_cursor;
            FETCH NEXT FROM target_cursor INTO @TableName, @ColumnName;
            WHILE @@FETCH_STATUS = 0
            BEGIN
                IF OBJECT_ID(N'dbo.' + @TableName, N'U') IS NOT NULL
                   AND COL_LENGTH(N'dbo.' + @TableName, @ColumnName) IS NOT NULL
                BEGIN
                    SELECT @ConstraintName = dc.name
                    FROM sys.default_constraints dc
                    JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
                    WHERE dc.parent_object_id = OBJECT_ID(N'dbo.' + @TableName)
                      AND c.name = @ColumnName;

                    IF @ConstraintName IS NOT NULL
                    BEGIN
                        SET @Sql = N'ALTER TABLE dbo.' + QUOTENAME(@TableName) +
                            N' DROP CONSTRAINT ' + QUOTENAME(@ConstraintName) + N';';
                        EXEC sys.sp_executesql @Sql;
                    END;

                    SET @Sql = N'ALTER TABLE dbo.' + QUOTENAME(@TableName) +
                        N' ADD DEFAULT SYSUTCDATETIME() FOR ' + QUOTENAME(@ColumnName) + N';';
                    EXEC sys.sp_executesql @Sql;
                END;

                SET @ConstraintName = NULL;
                FETCH NEXT FROM target_cursor INTO @TableName, @ColumnName;
            END;
            CLOSE target_cursor;
            DEALLOCATE target_cursor;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // UTC audit defaults are intentionally retained.
    }
}
