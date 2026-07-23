using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260723133000_AddSalaryScales")]
public sealed class AddSalaryScales : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'dbo.SalaryScales', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.SalaryScales
                (
                    Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_SalaryScales PRIMARY KEY,
                    TenantId int NOT NULL,
                    ScaleName nvarchar(100) NOT NULL,
                    BasicSalary decimal(18,2) NOT NULL CONSTRAINT DF_SalaryScales_BasicSalary DEFAULT(0),
                    MaximumSalary decimal(18,2) NOT NULL CONSTRAINT DF_SalaryScales_MaximumSalary DEFAULT(0),
                    YearlyIncrement decimal(18,2) NOT NULL CONSTRAINT DF_SalaryScales_YearlyIncrement DEFAULT(0),
                    MedicalAllowance decimal(18,2) NOT NULL CONSTRAINT DF_SalaryScales_MedicalAllowance DEFAULT(0),
                    TravellingAllowance decimal(18,2) NOT NULL CONSTRAINT DF_SalaryScales_TravellingAllowance DEFAULT(0),
                    Other decimal(18,2) NOT NULL CONSTRAINT DF_SalaryScales_Other DEFAULT(0),
                    IsActive bit NOT NULL CONSTRAINT DF_SalaryScales_IsActive DEFAULT(1),
                    CreatedDate datetime2 NOT NULL CONSTRAINT DF_SalaryScales_CreatedDate DEFAULT(SYSUTCDATETIME()),
                    ModifiedDate datetime2 NULL,
                    CONSTRAINT FK_SalaryScales_Tenants FOREIGN KEY(TenantId) REFERENCES dbo.Tenants(Id),
                    CONSTRAINT CK_SalaryScales_NonNegative CHECK
                    (
                        BasicSalary >= 0
                        AND MaximumSalary >= 0
                        AND YearlyIncrement >= 0
                        AND MedicalAllowance >= 0
                        AND TravellingAllowance >= 0
                        AND Other >= 0
                    )
                );
            END

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_SalaryScales_Tenant_ScaleName' AND object_id = OBJECT_ID(N'dbo.SalaryScales'))
                CREATE UNIQUE INDEX UX_SalaryScales_Tenant_ScaleName ON dbo.SalaryScales(TenantId, ScaleName);

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_SalaryScales_Tenant_IsActive' AND object_id = OBJECT_ID(N'dbo.SalaryScales'))
                CREATE INDEX IX_SalaryScales_Tenant_IsActive ON dbo.SalaryScales(TenantId, IsActive);

            DECLARE @platformId int;
            SELECT TOP(1) @platformId = Id
            FROM dbo.Menus
            WHERE Title = N'Platform Settings' AND ParentId IS NULL
            ORDER BY Id;

            IF @platformId IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM dbo.Menus WHERE Route = N'/settings/scales')
            BEGIN
                INSERT dbo.Menus (Title, Icon, Route, ParentId, SortOrder, IsActive)
                VALUES (N'Scale', N'BadgeDollarSign', N'/settings/scales', @platformId, 8, 1);
            END
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DELETE FROM dbo.Menus WHERE Route = N'/settings/scales';");
        migrationBuilder.Sql("DROP TABLE IF EXISTS dbo.SalaryScales;");
    }
}
