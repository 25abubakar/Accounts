using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260922120000_AddAnnualReportsModule")]
public sealed class AddAnnualReportsModule : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            SET ANSI_NULLS ON;
            SET QUOTED_IDENTIFIER ON;

            IF OBJECT_ID(N'dbo.AnnualReportTypes', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.AnnualReportTypes
                (
                    Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AnnualReportTypes PRIMARY KEY,
                    TenantId INT NULL,
                    Code NVARCHAR(40) NOT NULL,
                    Name NVARCHAR(80) NOT NULL,
                    IsActive BIT NOT NULL CONSTRAINT DF_AnnualReportTypes_IsActive DEFAULT (1),
                    CONSTRAINT FK_AnnualReportTypes_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id)
                );
                CREATE UNIQUE INDEX IX_AnnualReportTypes_Tenant_Code
                    ON dbo.AnnualReportTypes (TenantId, Code)
                    WHERE TenantId IS NOT NULL;
                CREATE UNIQUE INDEX IX_AnnualReportTypes_Global_Code
                    ON dbo.AnnualReportTypes (Code)
                    WHERE TenantId IS NULL;
            END;

            IF NOT EXISTS (SELECT 1 FROM dbo.AnnualReportTypes WHERE TenantId IS NULL AND Code = N'EXPENSE')
                INSERT INTO dbo.AnnualReportTypes (TenantId, Code, Name, IsActive) VALUES (NULL, N'EXPENSE', N'Expense', 1);
            IF NOT EXISTS (SELECT 1 FROM dbo.AnnualReportTypes WHERE TenantId IS NULL AND Code = N'INCOME')
                INSERT INTO dbo.AnnualReportTypes (TenantId, Code, Name, IsActive) VALUES (NULL, N'INCOME', N'Income', 1);

            IF OBJECT_ID(N'dbo.AnnualReportFilters', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.AnnualReportFilters
                (
                    Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AnnualReportFilters PRIMARY KEY,
                    TenantId INT NOT NULL,
                    ReportTypeId INT NOT NULL,
                    CategoryId INT NOT NULL,
                    IsInclude BIT NOT NULL CONSTRAINT DF_AnnualReportFilters_IsInclude DEFAULT (1),
                    CreatedByUserId NVARCHAR(450) NULL,
                    CreatedOnUtc DATETIME2 NOT NULL CONSTRAINT DF_AnnualReportFilters_CreatedOnUtc DEFAULT (SYSUTCDATETIME()),
                    UpdatedByUserId NVARCHAR(450) NULL,
                    UpdatedOnUtc DATETIME2 NULL,
                    CONSTRAINT FK_AnnualReportFilters_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id),
                    CONSTRAINT FK_AnnualReportFilters_Type FOREIGN KEY (ReportTypeId) REFERENCES dbo.AnnualReportTypes(Id),
                    CONSTRAINT FK_AnnualReportFilters_Category FOREIGN KEY (CategoryId) REFERENCES dbo.AccountsCategories(Id)
                );
                CREATE UNIQUE INDEX IX_AnnualReportFilters_Tenant_Type_Category
                    ON dbo.AnnualReportFilters (TenantId, ReportTypeId, CategoryId);
            END;

            IF OBJECT_ID(N'dbo.AnnualReportHeaders', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.AnnualReportHeaders
                (
                    Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AnnualReportHeaders PRIMARY KEY,
                    TenantId INT NOT NULL,
                    ReportTypeCode NVARCHAR(20) NOT NULL,
                    FiscalYear NVARCHAR(20) NOT NULL,
                    DateFrom DATE NOT NULL,
                    DateTo DATE NOT NULL,
                    Remarks NVARCHAR(2000) NULL,
                    IsApproved BIT NOT NULL CONSTRAINT DF_AnnualReportHeaders_IsApproved DEFAULT (0),
                    CreatedByUserId NVARCHAR(450) NULL,
                    CreatedOnUtc DATETIME2 NOT NULL CONSTRAINT DF_AnnualReportHeaders_CreatedOnUtc DEFAULT (SYSUTCDATETIME()),
                    UpdatedByUserId NVARCHAR(450) NULL,
                    UpdatedOnUtc DATETIME2 NULL,
                    ApprovedByUserId NVARCHAR(450) NULL,
                    ApprovedOnUtc DATETIME2 NULL,
                    CONSTRAINT FK_AnnualReportHeaders_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id)
                );
                CREATE UNIQUE INDEX IX_AnnualReportHeaders_Tenant_Kind_Fiscal
                    ON dbo.AnnualReportHeaders (TenantId, ReportTypeCode, FiscalYear);
            END;

            IF OBJECT_ID(N'dbo.AnnualReportLines', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.AnnualReportLines
                (
                    Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AnnualReportLines PRIMARY KEY,
                    HeaderId BIGINT NOT NULL,
                    CategoryId INT NOT NULL,
                    CategoryName NVARCHAR(120) NULL,
                    CalendarYear INT NOT NULL,
                    CalendarMonth INT NOT NULL,
                    Amount DECIMAL(18,2) NOT NULL CONSTRAINT DF_AnnualReportLines_Amount DEFAULT (0),
                    CONSTRAINT FK_AnnualReportLines_Header FOREIGN KEY (HeaderId) REFERENCES dbo.AnnualReportHeaders(Id) ON DELETE CASCADE,
                    CONSTRAINT FK_AnnualReportLines_Category FOREIGN KEY (CategoryId) REFERENCES dbo.AccountsCategories(Id)
                );
                CREATE INDEX IX_AnnualReportLines_Header ON dbo.AnnualReportLines (HeaderId);
            END;
            """);

        migrationBuilder.Sql(
            """
            CREATE OR ALTER PROCEDURE dbo.usp_AnnualReports_FilterList
                @TenantId INT
            AS
            BEGIN
                SET NOCOUNT ON;
                SELECT
                    f.Id,
                    f.ReportTypeId,
                    t.Name AS ReportType,
                    f.CategoryId,
                    c.Name AS Cat_Name,
                    f.IsInclude
                FROM dbo.AnnualReportFilters f
                INNER JOIN dbo.AnnualReportTypes t ON t.Id = f.ReportTypeId
                INNER JOIN dbo.AccountsCategories c ON c.Id = f.CategoryId AND c.TenantId = f.TenantId
                WHERE f.TenantId = @TenantId
                ORDER BY t.Name, c.Name, f.Id;
            END
            """);

        migrationBuilder.Sql(
            """
            CREATE OR ALTER PROCEDURE dbo.usp_AnnualReports_CategoryMonthMatrix
                @TenantId INT,
                @ReportTypeCode NVARCHAR(20),
                @DateFrom DATE,
                @DateTo DATE
            AS
            BEGIN
                SET NOCOUNT ON;

                ;WITH Included AS (
                    SELECT f.CategoryId, c.Name AS Cat_Name
                    FROM dbo.AnnualReportFilters f
                    INNER JOIN dbo.AnnualReportTypes t
                        ON t.Id = f.ReportTypeId
                       AND (t.TenantId IS NULL OR t.TenantId = @TenantId)
                    INNER JOIN dbo.AccountsCategories c
                        ON c.Id = f.CategoryId AND c.TenantId = f.TenantId
                    WHERE f.TenantId = @TenantId
                      AND f.IsInclude = 1
                      AND t.Code = @ReportTypeCode
                      AND c.IsActive = 1
                ),
                Amounts AS (
                    SELECT
                        e.CategoryId,
                        YEAR(e.TransDate) AS CalendarYear,
                        MONTH(e.TransDate) AS CalendarMonth,
                        SUM(ISNULL(e.Amount, 0)) AS Amount
                    FROM dbo.RoznamchaEntries e
                    INNER JOIN Included i ON i.CategoryId = e.CategoryId
                    WHERE e.TenantId = @TenantId
                      AND e.IsDeleted = 0
                      AND e.TransDate >= @DateFrom
                      AND e.TransDate <= @DateTo
                      AND e.CategoryId IS NOT NULL
                    GROUP BY e.CategoryId, YEAR(e.TransDate), MONTH(e.TransDate)
                )
                SELECT
                    i.CategoryId,
                    i.Cat_Name,
                    a.CalendarYear,
                    a.CalendarMonth,
                    ISNULL(a.Amount, 0) AS Amount
                FROM Included i
                LEFT JOIN Amounts a ON a.CategoryId = i.CategoryId
                ORDER BY i.Cat_Name, a.CalendarYear, a.CalendarMonth;
            END
            """);

        migrationBuilder.Sql(
            """
            CREATE OR ALTER PROCEDURE dbo.usp_AnnualReports_MonthWiseList
                @TenantId INT,
                @ReportTypeCode NVARCHAR(20)
            AS
            BEGIN
                SET NOCOUNT ON;

                SELECT
                    h.Id AS HeaderId,
                    h.FiscalYear,
                    h.Remarks,
                    h.IsApproved,
                    SUM(CASE WHEN l.CalendarMonth = 7 THEN l.Amount ELSE 0 END) AS Jul,
                    SUM(CASE WHEN l.CalendarMonth = 8 THEN l.Amount ELSE 0 END) AS Aug,
                    SUM(CASE WHEN l.CalendarMonth = 9 THEN l.Amount ELSE 0 END) AS Sep,
                    SUM(CASE WHEN l.CalendarMonth = 10 THEN l.Amount ELSE 0 END) AS Oct,
                    SUM(CASE WHEN l.CalendarMonth = 11 THEN l.Amount ELSE 0 END) AS Nov,
                    SUM(CASE WHEN l.CalendarMonth = 12 THEN l.Amount ELSE 0 END) AS Dec,
                    SUM(CASE WHEN l.CalendarMonth = 1 THEN l.Amount ELSE 0 END) AS Jan,
                    SUM(CASE WHEN l.CalendarMonth = 2 THEN l.Amount ELSE 0 END) AS Feb,
                    SUM(CASE WHEN l.CalendarMonth = 3 THEN l.Amount ELSE 0 END) AS Mar,
                    SUM(CASE WHEN l.CalendarMonth = 4 THEN l.Amount ELSE 0 END) AS Apr,
                    SUM(CASE WHEN l.CalendarMonth = 5 THEN l.Amount ELSE 0 END) AS May,
                    SUM(CASE WHEN l.CalendarMonth = 6 THEN l.Amount ELSE 0 END) AS Jun,
                    SUM(ISNULL(l.Amount, 0)) AS Total
                FROM dbo.AnnualReportHeaders h
                LEFT JOIN dbo.AnnualReportLines l ON l.HeaderId = h.Id
                WHERE h.TenantId = @TenantId
                  AND h.ReportTypeCode = @ReportTypeCode
                GROUP BY h.Id, h.FiscalYear, h.Remarks, h.IsApproved, h.CreatedOnUtc
                ORDER BY h.FiscalYear DESC, h.Id DESC;
            END
            """);

        migrationBuilder.Sql(
            """
            CREATE OR ALTER PROCEDURE dbo.usp_AnnualReports_CategoryWiseList
                @TenantId INT,
                @ReportTypeCode NVARCHAR(20)
            AS
            BEGIN
                SET NOCOUNT ON;

                SELECT
                    h.Id AS HeaderId,
                    h.FiscalYear,
                    l.CategoryId,
                    ISNULL(l.CategoryName, c.Name) AS Cat_Name,
                    SUM(ISNULL(l.Amount, 0)) AS Amount
                FROM dbo.AnnualReportHeaders h
                INNER JOIN dbo.AnnualReportLines l ON l.HeaderId = h.Id
                LEFT JOIN dbo.AccountsCategories c ON c.Id = l.CategoryId AND c.TenantId = h.TenantId
                WHERE h.TenantId = @TenantId
                  AND h.ReportTypeCode = @ReportTypeCode
                GROUP BY h.Id, h.FiscalYear, l.CategoryId, ISNULL(l.CategoryName, c.Name)
                ORDER BY h.FiscalYear DESC, Cat_Name;
            END
            """);

        migrationBuilder.Sql(
            """
            DECLARE @MenuIds table (MenuId int NOT NULL);
            INSERT INTO @MenuIds (MenuId)
            SELECT Id FROM dbo.Menus
            WHERE Route IN (
                N'/annual-reports/exp-report',
                N'/annual-reports/income-report',
                N'/annual-reports/filter');

            INSERT INTO dbo.Features (FeatureKey, FeatureName, Module, Description, CreatedDate)
            SELECT feature.FeatureKey, feature.FeatureName, N'Menu', feature.Description, SYSUTCDATETIME()
            FROM @MenuIds m
            CROSS APPLY (VALUES
                (CONCAT(N'MENU_', m.MenuId, N'_ADD'),    N'Annual Reports - Add',    N'Create annual report records.'),
                (CONCAT(N'MENU_', m.MenuId, N'_EDIT'),   N'Annual Reports - Edit',   N'Edit annual report records.'),
                (CONCAT(N'MENU_', m.MenuId, N'_DELETE'), N'Annual Reports - Delete', N'Delete annual report records.')
            ) feature(FeatureKey, FeatureName, Description)
            WHERE NOT EXISTS (SELECT 1 FROM dbo.Features existing WHERE existing.FeatureKey = feature.FeatureKey);

            INSERT INTO dbo.MenuPermissions (MenuId, PermissionId)
            SELECT m.MenuId, f.PermissionId
            FROM @MenuIds m
            JOIN dbo.Features f ON f.FeatureKey IN (
                CONCAT(N'MENU_', m.MenuId, N'_ADD'),
                CONCAT(N'MENU_', m.MenuId, N'_EDIT'),
                CONCAT(N'MENU_', m.MenuId, N'_DELETE'))
            WHERE NOT EXISTS (
                SELECT 1 FROM dbo.MenuPermissions existing
                WHERE existing.MenuId = m.MenuId AND existing.PermissionId = f.PermissionId);

            UPDATE tmp
            SET IsAllow = 1, CanView = 1, CanAdd = 1, CanEdit = 1, CanDelete = 1
            FROM dbo.TenantMenuPermissions tmp
            JOIN @MenuIds m ON m.MenuId = tmp.MenuId;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'dbo.usp_AnnualReports_CategoryWiseList', N'P') IS NOT NULL DROP PROCEDURE dbo.usp_AnnualReports_CategoryWiseList;
            IF OBJECT_ID(N'dbo.usp_AnnualReports_MonthWiseList', N'P') IS NOT NULL DROP PROCEDURE dbo.usp_AnnualReports_MonthWiseList;
            IF OBJECT_ID(N'dbo.usp_AnnualReports_CategoryMonthMatrix', N'P') IS NOT NULL DROP PROCEDURE dbo.usp_AnnualReports_CategoryMonthMatrix;
            IF OBJECT_ID(N'dbo.usp_AnnualReports_FilterList', N'P') IS NOT NULL DROP PROCEDURE dbo.usp_AnnualReports_FilterList;
            IF OBJECT_ID(N'dbo.AnnualReportLines', N'U') IS NOT NULL DROP TABLE dbo.AnnualReportLines;
            IF OBJECT_ID(N'dbo.AnnualReportHeaders', N'U') IS NOT NULL DROP TABLE dbo.AnnualReportHeaders;
            IF OBJECT_ID(N'dbo.AnnualReportFilters', N'U') IS NOT NULL DROP TABLE dbo.AnnualReportFilters;
            IF OBJECT_ID(N'dbo.AnnualReportTypes', N'U') IS NOT NULL DROP TABLE dbo.AnnualReportTypes;
            """);
    }
}
