using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260729103000_AddDefaultPayScales")]
public sealed class AddDefaultPayScales : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'dbo.SalaryScales', N'U') IS NOT NULL
            BEGIN
                IF COL_LENGTH(N'dbo.SalaryScales', N'DisplayOrder') IS NULL
                    ALTER TABLE dbo.SalaryScales ADD DisplayOrder int NOT NULL CONSTRAINT DF_SalaryScales_DisplayOrder DEFAULT(0);

                IF COL_LENGTH(N'dbo.SalaryScales', N'ScaleType') IS NULL
                    ALTER TABLE dbo.SalaryScales ADD ScaleType nvarchar(50) NOT NULL CONSTRAINT DF_SalaryScales_ScaleType DEFAULT(N'Regular');

                IF COL_LENGTH(N'dbo.SalaryScales', N'PayMode') IS NULL
                    ALTER TABLE dbo.SalaryScales ADD PayMode nvarchar(20) NOT NULL CONSTRAINT DF_SalaryScales_PayMode DEFAULT(N'PM');

                IF COL_LENGTH(N'dbo.SalaryScales', N'GrossSalary') IS NULL
                    ALTER TABLE dbo.SalaryScales ADD GrossSalary decimal(18,2) NOT NULL CONSTRAINT DF_SalaryScales_GrossSalary DEFAULT(0);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_SalaryScales_Tenant_DisplayOrder' AND object_id = OBJECT_ID(N'dbo.SalaryScales'))
                    CREATE INDEX IX_SalaryScales_Tenant_DisplayOrder ON dbo.SalaryScales(TenantId, DisplayOrder);
            END
            """);

        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'dbo.SalaryScales', N'U') IS NOT NULL
            BEGIN
                DECLARE @DefaultPayScales TABLE
                (
                    DisplayOrder int NOT NULL,
                    ScaleName nvarchar(100) NOT NULL,
                    BasicSalary decimal(18,2) NOT NULL,
                    YearlyIncrement decimal(18,2) NOT NULL,
                    ScaleType nvarchar(50) NOT NULL,
                    PayMode nvarchar(20) NOT NULL,
                    MaximumSalary decimal(18,2) NOT NULL,
                    GrossSalary decimal(18,2) NOT NULL
                );

                INSERT INTO @DefaultPayScales
                    (DisplayOrder, ScaleName, BasicSalary, YearlyIncrement, ScaleType, PayMode, MaximumSalary, GrossSalary)
                VALUES
                    (1,  N'RLT-1',   6000,   800, N'Regular',  N'PM',  10000,      0),
                    (2,  N'RLT-2',  10000,  1000, N'Regular',  N'PM',  15000,  26000),
                    (3,  N'RLT-3',  15000,  1000, N'Regular',  N'PM',  20000,  36000),
                    (4,  N'RLT-4',  20000,  1000, N'Regular',  N'PM',  25000,  46000),
                    (5,  N'RLT-5',  25000,  2000, N'Regular',  N'PM',  35000,  62000),
                    (6,  N'RLT-6',  35000,  2000, N'Regular',  N'PM',  45000,  82000),
                    (7,  N'RLT-7',  45000,  2000, N'Regular',  N'PM',  55000, 102000),
                    (8,  N'RLT-8',  55000,  3000, N'Regular',  N'PM',  70000, 128000),
                    (9,  N'RLT-9',  70000,  3000, N'Regular',  N'PM',  85000, 158000),
                    (10, N'RLT-10', 85000,  3000, N'Regular',  N'PM', 100000, 188000),
                    (11, N'RLT-11',100000,  6250, N'Regular',  N'PM', 125000, 231250),
                    (12, N'RLT-12',125000,  6250, N'Regular',  N'PM', 150000, 281250),
                    (13, N'RLT-13',150000,  6250, N'Regular',  N'PM', 175000, 331250),
                    (14, N'RLT-14',175000,  6250, N'Regular',  N'PM', 200000, 381250),
                    (15, N'RLT-15',200000,  6250, N'Regular',  N'PM', 250000, 456250),
                    (16, N'CLT-1',  10000,     0, N'Contract', N'PM',  10000,      0);

                MERGE dbo.SalaryScales AS target
                USING
                (
                    SELECT
                        tenant.Id AS TenantId,
                        scale.DisplayOrder,
                        scale.ScaleName,
                        scale.BasicSalary,
                        scale.YearlyIncrement,
                        scale.ScaleType,
                        scale.PayMode,
                        scale.MaximumSalary,
                        scale.GrossSalary
                    FROM dbo.Tenants AS tenant
                    CROSS JOIN @DefaultPayScales AS scale
                ) AS source
                ON target.TenantId = source.TenantId AND target.ScaleName = source.ScaleName
                WHEN MATCHED THEN
                    UPDATE SET
                        DisplayOrder = source.DisplayOrder,
                        BasicSalary = source.BasicSalary,
                        YearlyIncrement = source.YearlyIncrement,
                        ScaleType = source.ScaleType,
                        PayMode = source.PayMode,
                        MaximumSalary = source.MaximumSalary,
                        GrossSalary = source.GrossSalary,
                        IsActive = 1,
                        ModifiedDate = SYSUTCDATETIME()
                WHEN NOT MATCHED BY TARGET THEN
                    INSERT
                        (TenantId, ScaleName, DisplayOrder, BasicSalary, MaximumSalary, YearlyIncrement,
                         ScaleType, PayMode, GrossSalary, MedicalAllowance, TravellingAllowance, Other,
                         IsActive, CreatedDate)
                    VALUES
                        (source.TenantId, source.ScaleName, source.DisplayOrder, source.BasicSalary, source.MaximumSalary,
                         source.YearlyIncrement, source.ScaleType, source.PayMode, source.GrossSalary, 0, 0, 0,
                         1, SYSUTCDATETIME());
            END
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'dbo.SalaryScales', N'U') IS NOT NULL
            BEGIN
                DELETE FROM dbo.SalaryScales
                WHERE ScaleName IN
                (
                    N'RLT-1', N'RLT-2', N'RLT-3', N'RLT-4', N'RLT-5',
                    N'RLT-6', N'RLT-7', N'RLT-8', N'RLT-9', N'RLT-10',
                    N'RLT-11', N'RLT-12', N'RLT-13', N'RLT-14', N'RLT-15',
                    N'CLT-1'
                );
            END
            """);
    }
}
