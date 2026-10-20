using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class AddInternalTransferPairs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "InternalTransferGroupId",
                table: "RoznamchaEntries",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RoznamchaEntries_TenantId_InternalTransferGroupId_RoznamchaTypeId",
                table: "RoznamchaEntries",
                columns: new[] { "TenantId", "InternalTransferGroupId", "RoznamchaTypeId" },
                unique: true,
                filter: "[InternalTransferGroupId] IS NOT NULL");

            migrationBuilder.Sql(RoznamchaListSql.TypedPayment);
            migrationBuilder.Sql(RoznamchaListSql.TypedReceipt);
            migrationBuilder.Sql(RoznamchaListSql.TransferUnified);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(RoznamchaListSql.ConfiguredPayment);
            migrationBuilder.Sql(RoznamchaListSql.ConfiguredReceipt);
            migrationBuilder.Sql(RoznamchaListSql.ConfiguredUnified);
            migrationBuilder.DropIndex(
                name: "IX_RoznamchaEntries_TenantId_InternalTransferGroupId_RoznamchaTypeId",
                table: "RoznamchaEntries");

            migrationBuilder.DropColumn(
                name: "InternalTransferGroupId",
                table: "RoznamchaEntries");
        }
    }
}
