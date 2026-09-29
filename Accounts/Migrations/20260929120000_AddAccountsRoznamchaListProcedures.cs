using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

/// <summary>
/// Payment (refresh), Receipt, unified Roznamcha list SPs.
/// Sources: Sql/StoredProcedures/usp_Accounts_*Roz*.sql
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260929120000_AddAccountsRoznamchaListProcedures")]
public sealed class AddAccountsRoznamchaListProcedures : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE OR ALTER PROCEDURE dbo.usp_Accounts_PaymentRoz_List
                @TenantId INT,
                @DateFrom DATE,
                @DateTo DATE
            AS
            BEGIN
                SET NOCOUNT ON;

                DECLARE
                    @BillingCategoryId INT = NULL,
                    @PaymentRozTypeId INT = NULL;

                SELECT
                    @BillingCategoryId = s.BillingCategoryId,
                    @PaymentRozTypeId = s.PaymentRozTypeId
                FROM dbo.AccountsModuleSettings s
                WHERE s.TenantId = @TenantId;

                SELECT
                    e.Id,
                    e.SNo,
                    e.Ref,
                    e.OldRef,
                    rt.Name AS TypeName,
                    CAST(NULL AS NVARCHAR(200)) AS Project,
                    e.ProjectId,
                    toCat.Id AS ToCategoryId,
                    cat.Name AS CategoryName,
                    fromAcct.AccountName AS AccountName,
                    e.ToAccountId,
                    fromAcct.AccountNumber AS FromAccountNumber,
                    toCat.Name AS ToCategoryName,
                    toAcct.AccountName AS ToAccountName,
                    toAcct.AccountNumber AS ToAccountNumber,
                    tt.Name AS TransTypeName,
                    tm.Name AS TransModeName,
                    e.CreatedOnUtc AS CreatedDate,
                    e.BankRef,
                    e.InstrumentNo,
                    e.Descriptions,
                    e.TransDate,
                    e.Adjustment,
                    e.Amount AS Debit,
                    e.UsdAmount AS UsdDebit,
                    e.Qty,
                    e.Rate,
                    st.Name AS StatusName,
                    e.IsDeleted,
                    e.IsApproved,
                    e.IsLocked,
                    e.Attachment,
                    e.ImagePath AS [Image],
                    e.LibRef,
                    e.Remarks,
                    CAST(NULL AS DATETIME2) AS ProcessDate
                FROM dbo.RoznamchaEntries e
                LEFT JOIN dbo.AccountsRoznamchaTypes rt
                    ON rt.Id = e.RoznamchaTypeId
                   AND (rt.TenantId IS NULL OR rt.TenantId = e.TenantId)
                LEFT JOIN dbo.AccountsCategories cat
                    ON cat.Id = e.CategoryId
                   AND cat.TenantId = e.TenantId
                LEFT JOIN dbo.AccountsChartAccounts fromAcct
                    ON fromAcct.Id = e.FromAccountId
                   AND fromAcct.TenantId = e.TenantId
                LEFT JOIN dbo.AccountsChartAccounts toAcct
                    ON toAcct.Id = e.ToAccountId
                   AND toAcct.TenantId = e.TenantId
                LEFT JOIN dbo.AccountsCategories toCat
                    ON toCat.Id = toAcct.CategoryId
                   AND toCat.TenantId = e.TenantId
                LEFT JOIN dbo.AccountsTransTypes tt
                    ON tt.Id = e.TransTypeId
                   AND (tt.TenantId IS NULL OR tt.TenantId = e.TenantId)
                LEFT JOIN dbo.AccountsTransModes tm
                    ON tm.Id = e.TransModeId
                   AND (tm.TenantId IS NULL OR tm.TenantId = e.TenantId)
                LEFT JOIN dbo.AccountsEntryStatuses st
                    ON st.Id = e.EnterStatusId
                   AND (st.TenantId IS NULL OR st.TenantId = e.TenantId)
                WHERE e.TenantId = @TenantId
                  AND e.IsDeleted = 0
                  AND e.IsShow = 1
                  AND e.TransDate >= @DateFrom
                  AND e.TransDate <= @DateTo
                  AND (@BillingCategoryId IS NULL OR e.CategoryId = @BillingCategoryId)
                  AND (@PaymentRozTypeId IS NULL OR e.RoznamchaTypeId = @PaymentRozTypeId)
                ORDER BY e.TransDate, e.Id;
            END
            """);

        migrationBuilder.Sql(
            """
            CREATE OR ALTER PROCEDURE dbo.usp_Accounts_ReceiptRoz_List
                @TenantId INT,
                @DateFrom DATE,
                @DateTo DATE
            AS
            BEGIN
                SET NOCOUNT ON;

                DECLARE
                    @BillingCategoryId INT = NULL,
                    @ReceiptRozTypeId INT = NULL;

                SELECT
                    @BillingCategoryId = s.BillingCategoryId,
                    @ReceiptRozTypeId = s.ReceiptRozTypeId
                FROM dbo.AccountsModuleSettings s
                WHERE s.TenantId = @TenantId;

                SELECT
                    e.Id,
                    e.SNo,
                    e.Ref,
                    e.OldRef,
                    rt.Name AS TypeName,
                    CAST(NULL AS NVARCHAR(200)) AS Project,
                    e.ProjectId,
                    toCat.Id AS ToCategoryId,
                    cat.Name AS CategoryName,
                    fromAcct.AccountName AS AccountName,
                    e.ToAccountId,
                    fromAcct.AccountNumber AS FromAccountNumber,
                    toCat.Name AS ToCategoryName,
                    toAcct.AccountName AS ToAccountName,
                    toAcct.AccountNumber AS ToAccountNumber,
                    tt.Name AS TransTypeName,
                    tm.Name AS TransModeName,
                    e.CreatedOnUtc AS CreatedDate,
                    e.BankRef,
                    e.InstrumentNo,
                    e.Descriptions,
                    e.TransDate,
                    e.Adjustment,
                    e.Amount AS Debit,
                    e.UsdAmount AS UsdDebit,
                    e.Qty,
                    e.Rate,
                    st.Name AS StatusName,
                    e.IsDeleted,
                    e.IsApproved,
                    e.IsLocked,
                    e.Attachment,
                    e.ImagePath AS [Image],
                    e.LibRef,
                    e.Remarks,
                    CAST(NULL AS DATETIME2) AS ProcessDate
                FROM dbo.RoznamchaEntries e
                LEFT JOIN dbo.AccountsRoznamchaTypes rt
                    ON rt.Id = e.RoznamchaTypeId
                   AND (rt.TenantId IS NULL OR rt.TenantId = e.TenantId)
                LEFT JOIN dbo.AccountsCategories cat
                    ON cat.Id = e.CategoryId
                   AND cat.TenantId = e.TenantId
                LEFT JOIN dbo.AccountsChartAccounts fromAcct
                    ON fromAcct.Id = e.FromAccountId
                   AND fromAcct.TenantId = e.TenantId
                LEFT JOIN dbo.AccountsChartAccounts toAcct
                    ON toAcct.Id = e.ToAccountId
                   AND toAcct.TenantId = e.TenantId
                LEFT JOIN dbo.AccountsCategories toCat
                    ON toCat.Id = toAcct.CategoryId
                   AND toCat.TenantId = e.TenantId
                LEFT JOIN dbo.AccountsTransTypes tt
                    ON tt.Id = e.TransTypeId
                   AND (tt.TenantId IS NULL OR tt.TenantId = e.TenantId)
                LEFT JOIN dbo.AccountsTransModes tm
                    ON tm.Id = e.TransModeId
                   AND (tm.TenantId IS NULL OR tm.TenantId = e.TenantId)
                LEFT JOIN dbo.AccountsEntryStatuses st
                    ON st.Id = e.EnterStatusId
                   AND (st.TenantId IS NULL OR st.TenantId = e.TenantId)
                WHERE e.TenantId = @TenantId
                  AND e.IsDeleted = 0
                  AND e.IsShow = 1
                  AND e.TransDate >= @DateFrom
                  AND e.TransDate <= @DateTo
                  AND (@BillingCategoryId IS NULL OR e.CategoryId = @BillingCategoryId)
                  AND (@ReceiptRozTypeId IS NULL OR e.RoznamchaTypeId = @ReceiptRozTypeId)
                ORDER BY e.TransDate, e.Id;
            END
            """);

        migrationBuilder.Sql(
            """
            CREATE OR ALTER PROCEDURE dbo.usp_Accounts_Roznamcha_List
                @TenantId INT,
                @RozTypeId INT = NULL,
                @DateFrom DATE,
                @DateTo DATE
            AS
            BEGIN
                SET NOCOUNT ON;

                DECLARE @BillingCategoryId INT = NULL;

                SELECT @BillingCategoryId = s.BillingCategoryId
                FROM dbo.AccountsModuleSettings s
                WHERE s.TenantId = @TenantId;

                SELECT
                    e.Id,
                    e.SNo,
                    e.Ref,
                    e.OldRef,
                    rt.Name AS TypeName,
                    p.Name AS Project,
                    e.ProjectId,
                    toCat.Id AS ToCategoryId,
                    cat.Name AS CategoryName,
                    fromAcct.AccountName AS AccountName,
                    e.ToAccountId,
                    fromAcct.AccountNumber AS FromAccountNumber,
                    toCat.Name AS ToCategoryName,
                    toAcct.AccountName AS ToAccountName,
                    toAcct.AccountNumber AS ToAccountNumber,
                    tt.Name AS TransTypeName,
                    tm.Name AS TransModeName,
                    e.CreatedOnUtc AS CreatedDate,
                    e.BankRef,
                    e.InstrumentNo,
                    e.Descriptions,
                    e.TransDate,
                    e.Adjustment,
                    e.Amount AS Debit,
                    e.UsdAmount AS UsdDebit,
                    e.Qty,
                    e.Rate,
                    st.Name AS StatusName,
                    e.IsDeleted,
                    e.IsApproved,
                    e.IsLocked,
                    e.IsSettled,
                    e.Attachment,
                    e.ImagePath AS [Image],
                    e.LibRef,
                    e.Remarks,
                    procLog.ProcessDate
                FROM dbo.RoznamchaEntries e
                LEFT JOIN dbo.AccountsRoznamchaTypes rt
                    ON rt.Id = e.RoznamchaTypeId
                   AND (rt.TenantId IS NULL OR rt.TenantId = e.TenantId)
                LEFT JOIN dbo.AccountsProjects p
                    ON p.Id = e.ProjectId
                   AND p.TenantId = e.TenantId
                LEFT JOIN dbo.AccountsCategories cat
                    ON cat.Id = e.CategoryId
                   AND cat.TenantId = e.TenantId
                LEFT JOIN dbo.AccountsChartAccounts fromAcct
                    ON fromAcct.Id = e.FromAccountId
                   AND fromAcct.TenantId = e.TenantId
                LEFT JOIN dbo.AccountsChartAccounts toAcct
                    ON toAcct.Id = e.ToAccountId
                   AND toAcct.TenantId = e.TenantId
                LEFT JOIN dbo.AccountsCategories toCat
                    ON toCat.Id = toAcct.CategoryId
                   AND toCat.TenantId = e.TenantId
                LEFT JOIN dbo.AccountsTransTypes tt
                    ON tt.Id = e.TransTypeId
                   AND (tt.TenantId IS NULL OR tt.TenantId = e.TenantId)
                LEFT JOIN dbo.AccountsTransModes tm
                    ON tm.Id = e.TransModeId
                   AND (tm.TenantId IS NULL OR tm.TenantId = e.TenantId)
                LEFT JOIN dbo.AccountsEntryStatuses st
                    ON st.Id = e.EnterStatusId
                   AND (st.TenantId IS NULL OR st.TenantId = e.TenantId)
                OUTER APPLY (
                    SELECT TOP (1) l.ProcessedOnUtc AS ProcessDate
                    FROM dbo.RoznamchaEntryProcessLogs l
                    WHERE l.TenantId = e.TenantId
                      AND l.RoznamchaEntryId = e.Id
                    ORDER BY l.ProcessedOnUtc DESC, l.Id DESC
                ) procLog
                WHERE e.TenantId = @TenantId
                  AND e.IsDeleted = 0
                  AND e.IsShow = 1
                  AND e.TransDate >= @DateFrom
                  AND e.TransDate <= @DateTo
                  AND (@BillingCategoryId IS NULL OR e.CategoryId = @BillingCategoryId)
                  AND (@RozTypeId IS NULL OR e.RoznamchaTypeId = @RozTypeId)
                ORDER BY e.TransDate DESC, e.Id DESC;
            END
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'dbo.usp_Accounts_Roznamcha_List', N'P') IS NOT NULL
                DROP PROCEDURE dbo.usp_Accounts_Roznamcha_List;
            IF OBJECT_ID(N'dbo.usp_Accounts_ReceiptRoz_List', N'P') IS NOT NULL
                DROP PROCEDURE dbo.usp_Accounts_ReceiptRoz_List;
            """);
    }
}
