using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

/// <summary>
/// Payment (ROZ) list SP. Source: Sql/StoredProcedures/usp_Accounts_PaymentRoz_List.sql
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260915201000_AddAccountsPaymentRozListProcedure")]
public sealed class AddAccountsPaymentRozListProcedure : Migration
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
                    e.Remarks
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
                  AND e.TransDate >= @DateFrom
                  AND e.TransDate <= @DateTo
                  AND (@BillingCategoryId IS NULL OR e.CategoryId = @BillingCategoryId)
                  AND (@PaymentRozTypeId IS NULL OR e.RoznamchaTypeId = @PaymentRozTypeId)
                ORDER BY e.TransDate, e.Id;
            END
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'dbo.usp_Accounts_PaymentRoz_List', N'P') IS NOT NULL
                DROP PROCEDURE dbo.usp_Accounts_PaymentRoz_List;
            """);
    }
}
