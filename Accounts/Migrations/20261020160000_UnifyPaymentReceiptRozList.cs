using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261020160000_UnifyPaymentReceiptRozList")]
public sealed class UnifyPaymentReceiptRozList : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Start from the immutable last optimized definition so every DTO alias,
        // tenant join, configured status colour and library reference is retained.
        var payment = ((SqlOperation)new MaterializeRoznamchaAccountLookups().UpOperations[0]).Sql;
        var shared = payment.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("@TenantId INT, @DateFrom DATE, @DateTo DATE",
                "@TenantId INT, @DateFrom DATE, @DateTo DATE, @RoznamchaTypeId INT=NULL", StringComparison.Ordinal)
            .Replace("FROM dbo.AccountsModuleSettings WHERE TenantId=@TenantId;", """
                FROM dbo.AccountsModuleSettings WHERE TenantId=@TenantId;
                -- Three-argument callers remain payment-only during rollout.
                IF @RoznamchaTypeId IS NULL
                    SET @RoznamchaTypeId=COALESCE(@PaymentRozTypeId,
                        (SELECT TOP (1) Id FROM dbo.AccountsRoznamchaTypes WHERE UPPER(Code)=N'PAYMENT' ORDER BY Id));
                DECLARE @RequestedTypeCode nvarchar(50)=NULL;
                SELECT @RequestedTypeCode=UPPER(Code) FROM dbo.AccountsRoznamchaTypes WHERE Id=@RoznamchaTypeId;
                """, StringComparison.Ordinal)
            .Replace("OR EXISTS (", "OR (@RequestedTypeCode=N'PAYMENT' AND EXISTS (", StringComparison.Ordinal)
            .Replace("AND receipt.RoznamchaEntryId=e.Id\n          ))", "AND receipt.RoznamchaEntryId=e.Id\n          )))", StringComparison.Ordinal)
            .Replace("AND (@PaymentRozTypeId IS NULL OR e.RoznamchaTypeId=@PaymentRozTypeId) AND UPPER(rt.Code)=N'PAYMENT'",
                "AND e.RoznamchaTypeId=@RoznamchaTypeId AND @RequestedTypeCode IN (N'PAYMENT',N'RECEIPT')", StringComparison.Ordinal);
        migrationBuilder.Sql(shared);
        migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.usp_Accounts_ReceiptRoz_List;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Restore both previous definitions; no financial tables are modified.
        foreach (var operation in new MaterializeRoznamchaAccountLookups().UpOperations.Take(2))
            migrationBuilder.Operations.Add(operation);
    }
}
