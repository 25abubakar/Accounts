using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261020200000_UseConfiguredReceiptStatusColors")]
public sealed class UseConfiguredReceiptStatusColors : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Display-only inheritance: never return a Payment mapping ID for a Receipt.
        // An explicit Receipt mapping (even one deliberately without color) wins.
        var sql = ((SqlOperation)new UnifyPaymentReceiptRozList().UpOperations[0]).Sql;
        sql = sql.Replace("statusColor.ColorCode AS StatusColorCode",
            "CASE WHEN statusMap.Id IS NULL AND UPPER(rt.Code)=N'RECEIPT' THEN paymentStatusColor.ColorCode ELSE statusColor.ColorCode END AS StatusColorCode", StringComparison.Ordinal)
            .Replace("statusColor.FontColor AS StatusFontColor",
            "CASE WHEN statusMap.Id IS NULL AND UPPER(rt.Code)=N'RECEIPT' THEN paymentStatusColor.FontColor ELSE statusColor.FontColor END AS StatusFontColor", StringComparison.Ordinal)
            .Replace("LEFT JOIN PlatformSettings.Colors statusColor ON statusColor.Id=statusMap.ColorId", """
                LEFT JOIN PlatformSettings.Colors statusColor ON statusColor.Id=statusMap.ColorId
                LEFT JOIN PlatformSettings.Processes paymentColorProcess
                  ON UPPER(rt.Code)=N'RECEIPT' AND statusMap.Id IS NULL AND paymentColorProcess.Name=N'Payment'
                LEFT JOIN PlatformSettings.ProcessStatuses paymentColorMap
                  ON paymentColorMap.ProcessId=paymentColorProcess.Id AND paymentColorMap.StatusId=e.EnterStatusId
                LEFT JOIN PlatformSettings.Colors paymentStatusColor ON paymentStatusColor.Id=paymentColorMap.ColorId
                """, StringComparison.Ordinal);
        migrationBuilder.Sql(sql);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.Sql(((SqlOperation)new UnifyPaymentReceiptRozList().UpOperations[0]).Sql);
}
