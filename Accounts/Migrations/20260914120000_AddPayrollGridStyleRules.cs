using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260914120000_AddPayrollGridStyleRules")]
public sealed class AddPayrollGridStyleRules : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'dbo.PayrollGridStyleRules', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.PayrollGridStyleRules
                (
                    Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PayrollGridStyleRules PRIMARY KEY,
                    TenantId INT NOT NULL,
                    Category NVARCHAR(40) NOT NULL,
                    ColumnKey NVARCHAR(80) NOT NULL,
                    Caption NVARCHAR(80) NOT NULL,
                    BackgroundColor NVARCHAR(20) NOT NULL,
                    FontColor NVARCHAR(20) NOT NULL,
                    DisplayOrder INT NOT NULL CONSTRAINT DF_PayrollGridStyleRules_DisplayOrder DEFAULT (0),
                    IsActive BIT NOT NULL CONSTRAINT DF_PayrollGridStyleRules_IsActive DEFAULT (1),
                    CreatedOnUtc DATETIME2 NOT NULL CONSTRAINT DF_PayrollGridStyleRules_CreatedOnUtc DEFAULT (SYSUTCDATETIME()),
                    UpdatedOnUtc DATETIME2 NULL,
                    CONSTRAINT FK_PayrollGridStyleRules_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id)
                );
                CREATE UNIQUE INDEX IX_PayrollGridStyleRules_Tenant_Category_Column
                    ON dbo.PayrollGridStyleRules (TenantId, Category, ColumnKey);
                CREATE INDEX IX_PayrollGridStyleRules_Tenant_Category_Order
                    ON dbo.PayrollGridStyleRules (TenantId, Category, DisplayOrder);
            END;

            -- Seed legacy Staff Payroll colors for every tenant (idempotent).
            ;WITH Defaults AS (
                SELECT * FROM (VALUES
                    (N'ColumnHighlight', N'currentPay', N'CURRENT', N'#e4bcf5', N'#0F172A', 10),
                    (N'ColumnHighlight', N'grossPay', N'GROSS', N'#2BFA06', N'#0F172A', 20),
                    (N'ColumnHighlight', N'netPay', N'NET', N'#05F8F3', N'#0F172A', 30),
                    (N'ColumnHighlight', N'attendanceDeduction', N'DEDUCTION', N'#fff1f2', N'#be123c', 40),
                    (N'ColumnHighlight', N'effectiveAttendanceDeduction', N'NET ATT DED', N'#fff1f2', N'#be123c', 50),
                    (N'ColumnHighlight', N'totalDeduction', N'TOTAL DED', N'#fff1f2', N'#be123c', 60),
                    (N'ChangeHighlight', N'*', N'Changed vs previous month', N'#e5fc72', N'#0F172A', 10)
                ) AS d(Category, ColumnKey, Caption, BackgroundColor, FontColor, DisplayOrder)
            )
            INSERT INTO dbo.PayrollGridStyleRules
                (TenantId, Category, ColumnKey, Caption, BackgroundColor, FontColor, DisplayOrder, IsActive, CreatedOnUtc)
            SELECT t.Id, d.Category, d.ColumnKey, d.Caption, d.BackgroundColor, d.FontColor, d.DisplayOrder, 1, SYSUTCDATETIME()
            FROM dbo.Tenants t
            CROSS JOIN Defaults d
            WHERE NOT EXISTS (
                SELECT 1 FROM dbo.PayrollGridStyleRules r
                WHERE r.TenantId = t.Id AND r.Category = d.Category AND r.ColumnKey = d.ColumnKey
            );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'dbo.PayrollGridStyleRules', N'U') IS NOT NULL
                DROP TABLE dbo.PayrollGridStyleRules;
            """);
    }
}
