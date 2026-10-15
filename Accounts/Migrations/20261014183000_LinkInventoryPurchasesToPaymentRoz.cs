using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

/// <summary>
/// Posts each inventory stock receipt as a locked Payment Roznamcha row
/// and includes those rows on the Payment (ROZ) list even when the from-account
/// category is not the billing category.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20261014183000_LinkInventoryPurchasesToPaymentRoz")]
public sealed class LinkInventoryPurchasesToPaymentRoz : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(
            name: "RoznamchaEntryId",
            table: "SaleRoznamchaStockReceipts",
            type: "bigint",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_SaleRoznamchaStockReceipts_RoznamchaEntryId",
            table: "SaleRoznamchaStockReceipts",
            column: "RoznamchaEntryId",
            unique: true,
            filter: "[RoznamchaEntryId] IS NOT NULL");

        migrationBuilder.AddForeignKey(
            name: "FK_SaleRoznamchaStockReceipts_RoznamchaEntry",
            table: "SaleRoznamchaStockReceipts",
            column: "RoznamchaEntryId",
            principalTable: "RoznamchaEntries",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.Sql(SaleRoznamchaSql.PostInventoryPurchasePaymentProcedure);
        migrationBuilder.Sql(SaleRoznamchaSql.InventoryStockAddProcedure);
        migrationBuilder.Sql(RoznamchaListSql.Payment);
        migrationBuilder.Sql(RoznamchaListSql.Receipt);
        migrationBuilder.Sql(RoznamchaListSql.Unified);
        migrationBuilder.Sql(
            """
            SET NOCOUNT ON;
            DECLARE @TenantId int, @ReceiptId bigint;
            DECLARE receipts CURSOR LOCAL FAST_FORWARD FOR
                SELECT TenantId, Id
                FROM dbo.SaleRoznamchaStockReceipts
                WHERE RoznamchaEntryId IS NULL
                ORDER BY Id;
            OPEN receipts;
            FETCH NEXT FROM receipts INTO @TenantId, @ReceiptId;
            WHILE @@FETCH_STATUS = 0
            BEGIN
                EXEC dbo.usp_SaleRoznamcha_PostInventoryPurchasePayment
                    @TenantId = @TenantId,
                    @ReceiptId = @ReceiptId,
                    @UserId = NULL;
                FETCH NEXT FROM receipts INTO @TenantId, @ReceiptId;
            END;
            CLOSE receipts;
            DEALLOCATE receipts;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            SET NOCOUNT ON;
            DECLARE @Ids table (Id bigint NOT NULL PRIMARY KEY);
            INSERT @Ids (Id)
            SELECT RoznamchaEntryId
            FROM dbo.SaleRoznamchaStockReceipts
            WHERE RoznamchaEntryId IS NOT NULL;

            DELETE logs FROM dbo.RoznamchaEntryProcessLogs logs
            WHERE logs.RoznamchaEntryId IN (SELECT Id FROM @Ids);
            IF OBJECT_ID(N'dbo.RoznamchaLibraryDocuments', N'U') IS NOT NULL
                DELETE links FROM dbo.RoznamchaLibraryDocuments links
                WHERE links.RoznamchaEntryId IN (SELECT Id FROM @Ids);
            IF OBJECT_ID(N'dbo.AccountsEntryDocuments', N'U') IS NOT NULL
                DELETE docs FROM dbo.AccountsEntryDocuments docs
                WHERE docs.RoznamchaEntryId IN (SELECT Id FROM @Ids);

            UPDATE dbo.SaleRoznamchaStockReceipts
            SET RoznamchaEntryId = NULL
            WHERE RoznamchaEntryId IS NOT NULL;

            DELETE entries FROM dbo.RoznamchaEntries entries
            WHERE entries.Id IN (SELECT Id FROM @Ids);
            """);

        migrationBuilder.DropForeignKey(
            name: "FK_SaleRoznamchaStockReceipts_RoznamchaEntry",
            table: "SaleRoznamchaStockReceipts");

        migrationBuilder.DropIndex(
            name: "IX_SaleRoznamchaStockReceipts_RoznamchaEntryId",
            table: "SaleRoznamchaStockReceipts");

        migrationBuilder.DropColumn(
            name: "RoznamchaEntryId",
            table: "SaleRoznamchaStockReceipts");

        migrationBuilder.Sql(
            SaleRoznamchaSql.InventoryStockAddProcedure.Replace(
                """

                                EXEC dbo.usp_SaleRoznamcha_PostInventoryPurchasePayment
                                    @TenantId = @TenantId,
                                    @ReceiptId = @ReceiptId,
                                    @UserId = @UserId;
                """,
                string.Empty,
                StringComparison.Ordinal));
        migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.usp_SaleRoznamcha_PostInventoryPurchasePayment;");
        migrationBuilder.Sql(WithoutInventoryPostedVisibility(RoznamchaListSql.Payment));
        migrationBuilder.Sql(RoznamchaListSql.Receipt);
        migrationBuilder.Sql(WithoutInventoryPostedVisibility(RoznamchaListSql.Unified));
    }

    private static string WithoutInventoryPostedVisibility(string sql) =>
        sql.Replace(
            """
                  OR EXISTS (
                    SELECT 1 FROM dbo.SaleRoznamchaStockReceipts receipt
                    WHERE receipt.TenantId=e.TenantId AND receipt.RoznamchaEntryId=e.Id
                  )
            """,
            string.Empty,
            StringComparison.Ordinal);
}
