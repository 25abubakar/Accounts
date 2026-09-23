using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountsAnnualReportAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "AnnualReportHeaderId",
                table: "AccountsEntryDocuments",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountsEntryDocuments_AnnualReportHeaderId",
                table: "AccountsEntryDocuments",
                column: "AnnualReportHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountsEntryDocuments_TenantId_AnnualReportHeaderId",
                table: "AccountsEntryDocuments",
                columns: new[] { "TenantId", "AnnualReportHeaderId" });

            migrationBuilder.AddForeignKey(
                name: "FK_AccountsEntryDocuments_AnnualReportHeaders_AnnualReportHeaderId",
                table: "AccountsEntryDocuments",
                column: "AnnualReportHeaderId",
                principalTable: "AnnualReportHeaders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AccountsEntryDocuments_AnnualReportHeaders_AnnualReportHeaderId",
                table: "AccountsEntryDocuments");

            migrationBuilder.DropIndex(
                name: "IX_AccountsEntryDocuments_AnnualReportHeaderId",
                table: "AccountsEntryDocuments");

            migrationBuilder.DropIndex(
                name: "IX_AccountsEntryDocuments_TenantId_AnnualReportHeaderId",
                table: "AccountsEntryDocuments");

            migrationBuilder.DropColumn(
                name: "AnnualReportHeaderId",
                table: "AccountsEntryDocuments");
        }
    }
}
