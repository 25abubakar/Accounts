using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

/// <summary>
/// Statements parent + Bank child menus, and bank statement list SP.
/// Source: Sql/StoredProcedures/usp_Accounts_BankStatementList.sql
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260923160000_AddStatementsBankModule")]
public sealed class AddStatementsBankModule : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            SET NOCOUNT ON;
            SET ANSI_NULLS ON;
            SET QUOTED_IDENTIFIER ON;

            DECLARE @ParentId int = (
                SELECT TOP (1) Id
                FROM dbo.Menus
                WHERE ParentId IS NULL AND Title = N'Statements'
                ORDER BY Id
            );

            IF @ParentId IS NULL
            BEGIN
                INSERT INTO dbo.Menus (Title, Icon, Route, ParentId, SortOrder, IsActive)
                VALUES (N'Statements', N'Users', NULL, NULL, 86, 1);
                SET @ParentId = SCOPE_IDENTITY();
            END;
            ELSE
                UPDATE dbo.Menus
                SET Title = N'Statements', Icon = N'Users', Route = NULL,
                    ParentId = NULL, SortOrder = 86, IsActive = 1
                WHERE Id = @ParentId;

            DECLARE @BankId int = (
                SELECT TOP (1) Id
                FROM dbo.Menus
                WHERE Route = N'/statements/bank'
                   OR (ParentId = @ParentId AND Title = N'Bank')
                ORDER BY CASE WHEN Route = N'/statements/bank' THEN 0 ELSE 1 END, Id
            );

            IF @BankId IS NULL
            BEGIN
                INSERT INTO dbo.Menus (Title, Icon, Route, ParentId, SortOrder, IsActive)
                VALUES (N'Bank', N'Landmark', N'/statements/bank', @ParentId, 1, 1);
                SET @BankId = SCOPE_IDENTITY();
            END;
            ELSE
                UPDATE dbo.Menus
                SET Title = N'Bank', Icon = N'Landmark', Route = N'/statements/bank',
                    ParentId = @ParentId, SortOrder = 1, IsActive = 1
                WHERE Id = @BankId;

            DECLARE @Targets table (MenuId int NOT NULL, Title nvarchar(100) NOT NULL);
            INSERT INTO @Targets (MenuId, Title) VALUES (@BankId, N'Bank');

            INSERT INTO dbo.Features (FeatureKey, FeatureName, Module, Description, CreatedDate)
            SELECT CONCAT(N'MENU_', target.MenuId), target.Title, N'Menu',
                   CONCAT(N'Open the ', target.Title, N' statement screen.'), SYSUTCDATETIME()
            FROM @Targets target
            WHERE NOT EXISTS (
                SELECT 1 FROM dbo.Features feature
                WHERE feature.FeatureKey = CONCAT(N'MENU_', target.MenuId)
            );

            INSERT INTO dbo.Features (FeatureKey, FeatureName, Module, Description, CreatedDate)
            SELECT feature.FeatureKey, feature.FeatureName, N'Menu', feature.Description, SYSUTCDATETIME()
            FROM (
                SELECT CONCAT(N'MENU_', t.MenuId, N'_VIEW') AS FeatureKey, CONCAT(t.Title, N' View') AS FeatureName, CONCAT(N'View ', t.Title, N' statements.') AS Description FROM @Targets t
                UNION ALL SELECT CONCAT(N'MENU_', t.MenuId, N'_ADD'), CONCAT(t.Title, N' Add'), CONCAT(N'Add ', t.Title, N' statements.') FROM @Targets t
                UNION ALL SELECT CONCAT(N'MENU_', t.MenuId, N'_EDIT'), CONCAT(t.Title, N' Edit'), CONCAT(N'Edit ', t.Title, N' statements.') FROM @Targets t
                UNION ALL SELECT CONCAT(N'MENU_', t.MenuId, N'_DELETE'), CONCAT(t.Title, N' Delete'), CONCAT(N'Delete ', t.Title, N' statements.') FROM @Targets t
            ) feature
            WHERE NOT EXISTS (SELECT 1 FROM dbo.Features f WHERE f.FeatureKey = feature.FeatureKey);

            INSERT INTO dbo.MenuPermissions (MenuId, PermissionId)
            SELECT t.MenuId, f.PermissionId
            FROM @Targets t
            INNER JOIN dbo.Features f ON f.FeatureKey IN (
                CONCAT(N'MENU_', t.MenuId),
                CONCAT(N'MENU_', t.MenuId, N'_VIEW'),
                CONCAT(N'MENU_', t.MenuId, N'_ADD'),
                CONCAT(N'MENU_', t.MenuId, N'_EDIT'),
                CONCAT(N'MENU_', t.MenuId, N'_DELETE')
            )
            WHERE NOT EXISTS (
                SELECT 1 FROM dbo.MenuPermissions mp
                WHERE mp.MenuId = t.MenuId AND mp.PermissionId = f.PermissionId
            );

            INSERT INTO dbo.TenantMenuPermissions (TenantId, MenuId, CanView, CanAdd, CanEdit, CanDelete, IsAllow, GrantedOnUtc)
            SELECT ten.Id, t.MenuId, 1, 1, 1, 1, 1, SYSUTCDATETIME()
            FROM dbo.Tenants ten
            CROSS JOIN @Targets t
            WHERE NOT EXISTS (
                SELECT 1 FROM dbo.TenantMenuPermissions tmp
                WHERE tmp.TenantId = ten.Id AND tmp.MenuId = t.MenuId
            );
            """);

        migrationBuilder.Sql(
            """
            CREATE OR ALTER PROCEDURE dbo.usp_Accounts_BankStatementList
                @TenantId INT,
                @AccountId INT = NULL,
                @DateFrom DATE = NULL,
                @DateTo DATE = NULL
            AS
            BEGIN
                SET NOCOUNT ON;

                SELECT
                    s.Id,
                    s.ChartAccountId AS AccountId,
                    cat.AccountName AS AccountName,
                    s.AccountNumber AS AccountReference,
                    s.ValueDate,
                    s.PostingDate,
                    s.InstrumentNo,
                    s.Description AS TransactionDetails,
                    s.TransactionReferenceNumber AS TransactionReferenceNo,
                    ISNULL(s.Debit, 0) AS Debit,
                    ISNULL(s.Credit, 0) AS Credit,
                    ISNULL(s.Balance, 0) AS Balance,
                    s.Remarks,
                    s.ReferenceNumber AS ReferenceNo,
                    s.Attachment,
                    s.IsSettled,
                    s.IsReversal,
                    s.IsManual,
                    s.IsMatched,
                    s.StatusId,
                    s.DateFormat
                FROM dbo.BankStatements s
                LEFT JOIN dbo.AccountsChartAccounts cat
                    ON cat.Id = s.ChartAccountId
                   AND cat.TenantId = s.TenantId
                WHERE s.TenantId = @TenantId
                  AND (@AccountId IS NULL OR s.ChartAccountId = @AccountId)
                  AND (
                        @DateFrom IS NULL
                        OR ISNULL(s.PostingDate, ISNULL(s.ValueDate, s.StatementDate)) >= @DateFrom
                      )
                  AND (
                        @DateTo IS NULL
                        OR ISNULL(s.PostingDate, ISNULL(s.ValueDate, s.StatementDate)) <= @DateTo
                      )
                ORDER BY
                    ISNULL(s.PostingDate, ISNULL(s.ValueDate, s.StatementDate)) DESC,
                    s.Id DESC;
            END
            """);

        migrationBuilder.Sql(
            """
            SET QUOTED_IDENTIFIER ON;
            SET ANSI_NULLS ON;

            IF NOT EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE name = N'IX_BankStatements_Tenant_Account_Posting'
                  AND object_id = OBJECT_ID(N'dbo.BankStatements'))
            BEGIN
                CREATE INDEX IX_BankStatements_Tenant_Account_Posting
                    ON dbo.BankStatements (TenantId, ChartAccountId, PostingDate, ValueDate)
                    INCLUDE (Debit, Credit, Balance, ReferenceNumber, Description);
            END
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'dbo.usp_Accounts_BankStatementList', N'P') IS NOT NULL
                DROP PROCEDURE dbo.usp_Accounts_BankStatementList;

            IF EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE name = N'IX_BankStatements_Tenant_Account_Posting'
                  AND object_id = OBJECT_ID(N'dbo.BankStatements'))
                DROP INDEX IX_BankStatements_Tenant_Account_Posting ON dbo.BankStatements;

            DECLARE @BankId int = (SELECT TOP 1 Id FROM dbo.Menus WHERE Route = N'/statements/bank');
            DECLARE @ParentId int = (SELECT TOP 1 Id FROM dbo.Menus WHERE ParentId IS NULL AND Title = N'Statements');

            IF @BankId IS NOT NULL
            BEGIN
                DELETE FROM dbo.TenantMenuPermissions WHERE MenuId = @BankId;
                DELETE FROM dbo.MenuPermissions WHERE MenuId = @BankId;
                DELETE FROM dbo.Features WHERE FeatureKey LIKE CONCAT(N'MENU_', @BankId, N'%');
                DELETE FROM dbo.Menus WHERE Id = @BankId;
            END

            IF @ParentId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.Menus WHERE ParentId = @ParentId)
            BEGIN
                DELETE FROM dbo.TenantMenuPermissions WHERE MenuId = @ParentId;
                DELETE FROM dbo.MenuPermissions WHERE MenuId = @ParentId;
                DELETE FROM dbo.Features WHERE FeatureKey LIKE CONCAT(N'MENU_', @ParentId, N'%');
                DELETE FROM dbo.Menus WHERE Id = @ParentId;
            END
            """);
    }
}
