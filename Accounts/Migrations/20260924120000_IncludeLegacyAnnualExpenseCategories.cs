using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

/// <summary>
/// Includes the remaining expense categories that are present in the legacy
/// annual expense report. The migration only updates the current Accounts
/// database and keeps tenant-owned filter rows isolated by TenantId.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260924120000_IncludeLegacyAnnualExpenseCategories")]
public sealed class IncludeLegacyAnnualExpenseCategories : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            SET XACT_ABORT ON;

            IF OBJECT_ID(N'dbo.AnnualReportFilters', N'U') IS NULL
                RETURN;

            DECLARE @ExpenseTypeId INT =
            (
                SELECT TOP (1) Id
                FROM dbo.AnnualReportTypes
                WHERE Code = N'EXPENSE' AND IsActive = 1
                ORDER BY CASE WHEN TenantId IS NULL THEN 0 ELSE 1 END, Id
            );

            IF @ExpenseTypeId IS NULL
                RETURN;

            DECLARE @IncludedCategories TABLE (CategoryName NVARCHAR(120) NOT NULL PRIMARY KEY);

            INSERT INTO @IncludedCategories (CategoryName) VALUES
                (N'SAASC'),
                (N'EOBI'),
                (N'Entertainment'),
                (N'Rent Rate&Taxes'),
                (N'Internet');

            ;WITH Resolved AS
            (
                SELECT c.TenantId, @ExpenseTypeId AS ReportTypeId, c.Id AS CategoryId
                FROM dbo.AccountsCategories c
                INNER JOIN dbo.Tenants tenant ON tenant.Id = c.TenantId AND tenant.IsActive = 1
                INNER JOIN @IncludedCategories seed
                    ON LOWER(LTRIM(RTRIM(c.Name))) = LOWER(LTRIM(RTRIM(seed.CategoryName)))
                WHERE c.IsActive = 1
            )
            MERGE dbo.AnnualReportFilters AS target
            USING Resolved AS source
                ON target.TenantId = source.TenantId
               AND target.ReportTypeId = source.ReportTypeId
               AND target.CategoryId = source.CategoryId
            WHEN MATCHED AND target.IsInclude = 0 THEN
                UPDATE SET IsInclude = 1, UpdatedOnUtc = SYSUTCDATETIME()
            WHEN NOT MATCHED BY TARGET THEN
                INSERT (TenantId, ReportTypeId, CategoryId, IsInclude, CreatedOnUtc)
                VALUES (source.TenantId, source.ReportTypeId, source.CategoryId, 1, SYSUTCDATETIME());
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Keep report-filter selections because they are tenant business data.
    }
}
