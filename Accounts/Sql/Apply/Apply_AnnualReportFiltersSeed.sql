-- Idempotent seed for Annual Report Filters (screenshot parity).
-- Maps existing AccountsCategories → AnnualReportFilters with IsInclude flags.
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

IF OBJECT_ID(N'dbo.AnnualReportFilters', N'U') IS NULL
BEGIN
    RAISERROR(N'AnnualReportFilters table is missing. Run Apply_AnnualReportsModule.sql first.', 16, 1);
    RETURN;
END;

IF NOT EXISTS (SELECT 1 FROM dbo.AnnualReportTypes WHERE TenantId IS NULL AND Code = N'EXPENSE')
    INSERT INTO dbo.AnnualReportTypes (TenantId, Code, Name, IsActive) VALUES (NULL, N'EXPENSE', N'Expense', 1);
IF NOT EXISTS (SELECT 1 FROM dbo.AnnualReportTypes WHERE TenantId IS NULL AND Code = N'INCOME')
    INSERT INTO dbo.AnnualReportTypes (TenantId, Code, Name, IsActive) VALUES (NULL, N'INCOME', N'Income', 1);

DECLARE @ExpenseTypeId INT = (SELECT TOP 1 Id FROM dbo.AnnualReportTypes WHERE Code = N'EXPENSE' AND IsActive = 1 ORDER BY CASE WHEN TenantId IS NULL THEN 0 ELSE 1 END, Id);
DECLARE @IncomeTypeId INT  = (SELECT TOP 1 Id FROM dbo.AnnualReportTypes WHERE Code = N'INCOME'  AND IsActive = 1 ORDER BY CASE WHEN TenantId IS NULL THEN 0 ELSE 1 END, Id);

IF @ExpenseTypeId IS NULL OR @IncomeTypeId IS NULL
BEGIN
    RAISERROR(N'AnnualReportTypes Expense/Income are missing.', 16, 1);
    RETURN;
END;

;WITH SeedMap AS
(
    SELECT * FROM (VALUES
        -- Expense (ReportType = Expense)
        (N'EXPENSE', N'Expense',                    CAST(1 AS BIT)),
        (N'EXPENSE', N'Fee & Subscriptions',        CAST(1 AS BIT)),
        (N'EXPENSE', N'Printing & Stationary',      CAST(1 AS BIT)),
        (N'EXPENSE', N'Repair & Maintenance',       CAST(1 AS BIT)),
        (N'EXPENSE', N'Travel & Conveyance',        CAST(1 AS BIT)),
        (N'EXPENSE', N'Donations',                  CAST(1 AS BIT)),
        (N'EXPENSE', N'Clients',                    CAST(1 AS BIT)),
        (N'EXPENSE', N'Projects',                   CAST(1 AS BIT)),
        (N'EXPENSE', N'Salary & Benefits',          CAST(1 AS BIT)),
        (N'EXPENSE', N'Assets & inventory',         CAST(1 AS BIT)),
        (N'EXPENSE', N'Loan & Advance',             CAST(0 AS BIT)),
        (N'EXPENSE', N'Suppliers & Services',       CAST(1 AS BIT)),
        (N'EXPENSE', N'Partners',                   CAST(0 AS BIT)),
        (N'EXPENSE', N'Account & Reconciliation',   CAST(0 AS BIT)),
        (N'EXPENSE', N'Constructions',              CAST(0 AS BIT)),
        (N'EXPENSE', N'SAASC',                      CAST(0 AS BIT)),
        (N'EXPENSE', N'LT Staff',                   CAST(0 AS BIT)),
        (N'EXPENSE', N'Misc Exp',                   CAST(1 AS BIT)),
        (N'EXPENSE', N'Tax',                        CAST(1 AS BIT)),
        (N'EXPENSE', N'Utilities',                  CAST(1 AS BIT)),
        (N'EXPENSE', N'Bank Charges',               CAST(1 AS BIT)),
        (N'EXPENSE', N'Arsh Studio',                CAST(1 AS BIT)),
        (N'EXPENSE', N'Admin',                      CAST(1 AS BIT)),
        (N'EXPENSE', N'Amazon (H)',                 CAST(1 AS BIT)),
        -- Income (ReportType = Income)
        (N'INCOME',  N'Cash in Hand',               CAST(0 AS BIT)),
        (N'INCOME',  N'Clients',                    CAST(1 AS BIT)),
        (N'INCOME',  N'Bank',                       CAST(0 AS BIT))
    ) AS v(ReportTypeCode, CategoryName, IsInclude)
),
Resolved AS
(
    SELECT
        c.TenantId,
        CASE WHEN s.ReportTypeCode = N'EXPENSE' THEN @ExpenseTypeId ELSE @IncomeTypeId END AS ReportTypeId,
        c.Id AS CategoryId,
        s.IsInclude
    FROM SeedMap s
    INNER JOIN dbo.AccountsCategories c
        ON LOWER(LTRIM(RTRIM(c.Name))) = LOWER(LTRIM(RTRIM(s.CategoryName)))
       AND c.IsActive = 1
    INNER JOIN dbo.Tenants t ON t.Id = c.TenantId AND t.IsActive = 1
)
MERGE dbo.AnnualReportFilters AS target
USING Resolved AS src
    ON target.TenantId = src.TenantId
   AND target.ReportTypeId = src.ReportTypeId
   AND target.CategoryId = src.CategoryId
WHEN MATCHED AND target.IsInclude <> src.IsInclude THEN
    UPDATE SET
        IsInclude = src.IsInclude,
        UpdatedOnUtc = SYSUTCDATETIME()
WHEN NOT MATCHED BY TARGET THEN
    INSERT (TenantId, ReportTypeId, CategoryId, IsInclude, CreatedOnUtc)
    VALUES (src.TenantId, src.ReportTypeId, src.CategoryId, src.IsInclude, SYSUTCDATETIME());

SELECT
    t.Id AS TenantId,
    t.TenantName,
    COUNT(*) AS FilterCount,
    SUM(CASE WHEN f.IsInclude = 1 THEN 1 ELSE 0 END) AS IncludedCount
FROM dbo.AnnualReportFilters f
INNER JOIN dbo.Tenants t ON t.Id = f.TenantId
GROUP BY t.Id, t.TenantName
ORDER BY t.Id;
