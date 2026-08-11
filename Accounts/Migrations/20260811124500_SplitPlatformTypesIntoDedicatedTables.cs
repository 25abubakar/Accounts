using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260811124500_SplitPlatformTypesIntoDedicatedTables")]
public sealed class SplitPlatformTypesIntoDedicatedTables : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF NOT EXISTS (SELECT 1 FROM sys.sequences WHERE name=N'PlatformTypeTableRowSequence' AND schema_id=SCHEMA_ID(N'dbo'))
                EXEC(N'CREATE SEQUENCE dbo.PlatformTypeTableRowSequence AS int START WITH 1 INCREMENT BY 1');

            DECLARE @masters TABLE (TableName sysname NOT NULL, CategoryCode nvarchar(50) NOT NULL);
            INSERT @masters (TableName,CategoryCode) VALUES
              (N'ContractTypes',N'CONTRACT'),
              (N'FrequencyTypes',N'FREQUENCY'),
              (N'RateTypes',N'RATE'),
              (N'AllowanceTypes',N'ALLOWANCE_TYPE'),
              (N'TadaTypes',N'TADA_TYPE'),
              (N'LeaveTypes',N'LEAVE_TYPE'),
              (N'AnnouncementTypes',N'ANNOUNCEMENT_TYPE'),
              (N'AssessmentTypes',N'ASSESSMENT_TYPE'),
              (N'AttendanceTypes',N'ATTENDANCE_TYPE'),
              (N'BenefitTypes',N'BENEFITS_TYPE');

            DECLARE @table sysname, @category nvarchar(50), @sql nvarchar(max);
            DECLARE master_cursor CURSOR LOCAL FAST_FORWARD FOR SELECT TableName,CategoryCode FROM @masters;
            OPEN master_cursor;
            FETCH NEXT FROM master_cursor INTO @table,@category;
            WHILE @@FETCH_STATUS=0
            BEGIN
                IF OBJECT_ID(N'dbo.' + @table,N'U') IS NULL
                BEGIN
                    SET @sql=N'CREATE TABLE dbo.' + QUOTENAME(@table) + N'(
                        Id int NOT NULL CONSTRAINT PK_' + @table + N' PRIMARY KEY
                            CONSTRAINT DF_' + @table + N'_Id DEFAULT (NEXT VALUE FOR dbo.PlatformTypeTableRowSequence),
                        TenantId int NOT NULL,
                        Name nvarchar(150) NOT NULL,
                        Code nvarchar(100) NOT NULL,
                        DisplayOrder int NOT NULL CONSTRAINT DF_' + @table + N'_DisplayOrder DEFAULT(0),
                        IsActive bit NOT NULL CONSTRAINT DF_' + @table + N'_IsActive DEFAULT(1),
                        CreatedOnUtc datetime2 NOT NULL CONSTRAINT DF_' + @table + N'_CreatedOnUtc DEFAULT(SYSUTCDATETIME()),
                        ModifiedOnUtc datetime2 NULL,
                        CreatedByUserId nvarchar(450) NULL,
                        ModifiedByUserId nvarchar(450) NULL,
                        CONSTRAINT FK_' + @table + N'_Tenants_TenantId FOREIGN KEY(TenantId) REFERENCES dbo.Tenants(Id)
                    );
                    CREATE UNIQUE INDEX IX_' + @table + N'_TenantId_Code ON dbo.' + QUOTENAME(@table) + N'(TenantId,Code);
                    CREATE INDEX IX_' + @table + N'_TenantId_DisplayOrder ON dbo.' + QUOTENAME(@table) + N'(TenantId,DisplayOrder);';
                    EXEC sys.sp_executesql @sql;
                END

                -- Transfer the existing tenant data once. The category/code
                -- pair is unique, so rerunning startup remains idempotent.
                SET @sql=N'INSERT dbo.' + QUOTENAME(@table) + N'
                    (Id,TenantId,Name,Code,DisplayOrder,IsActive,CreatedOnUtc,ModifiedOnUtc,CreatedByUserId,ModifiedByUserId)
                    SELECT value.Id,value.TenantId,value.Name,value.Code,value.DisplayOrder,value.IsActive,
                           value.CreatedOnUtc,value.ModifiedOnUtc,value.CreatedByUserId,value.ModifiedByUserId
                    FROM dbo.PlatformTypeValues value
                    JOIN dbo.PlatformTypeCategories category ON category.Id=value.CategoryId
                    WHERE category.Code=@category
                      AND NOT EXISTS (SELECT 1 FROM dbo.' + QUOTENAME(@table) + N' target
                                      WHERE target.TenantId=value.TenantId AND target.Code=value.Code);';
                EXEC sys.sp_executesql @sql,N'@category nvarchar(50)',@category;

                FETCH NEXT FROM master_cursor INTO @table,@category;
            END
            CLOSE master_cursor;
            DEALLOCATE master_cursor;

            DECLARE @nextId int=ISNULL((SELECT MAX(Id)+1 FROM dbo.PlatformTypeValues),1);
            SET @sql=N'ALTER SEQUENCE dbo.PlatformTypeTableRowSequence RESTART WITH ' + CONVERT(nvarchar(20),@nextId);
            EXEC sys.sp_executesql @sql;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Deliberately non-destructive. These master tables can become FK
        // targets for future payroll/HR modules, so an automatic rollback must
        // never remove their data. A reviewed archival migration can be used
        // later if the architecture is intentionally changed again.
    }
}
