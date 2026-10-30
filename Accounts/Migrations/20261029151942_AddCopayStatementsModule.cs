using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class AddCopayStatementsModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CopayStatements",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    Ref = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    AcctId = table.Column<int>(type: "int", nullable: true),
                    CardNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Currency = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    PaymentNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PaymentDate = table.Column<DateOnly>(type: "date", nullable: true),
                    TransactionAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    FundSource = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    BusinessName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    MerchantID = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PaymentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    AdjustmentDescription = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    GBOK_BatchID = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    InterchangeDescription = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ARNNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CopayRef = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Attachment = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Remarks = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    IsSettled = table.Column<bool>(type: "bit", nullable: false),
                    StatusId = table.Column<int>(type: "int", nullable: true),
                    IsManual = table.Column<bool>(type: "bit", nullable: false),
                    MatchedRoznamchaEntryId = table.Column<long>(type: "bigint", nullable: true),
                    YearId = table.Column<int>(type: "int", nullable: true),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CopayStatements", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CopayStatements_TenantId_IsSettled_StatusId",
                table: "CopayStatements",
                columns: new[] { "TenantId", "IsSettled", "StatusId" });

            migrationBuilder.CreateIndex(
                name: "IX_CopayStatements_TenantId_MatchedRoznamchaEntryId",
                table: "CopayStatements",
                columns: new[] { "TenantId", "MatchedRoznamchaEntryId" });

            migrationBuilder.CreateIndex(
                name: "IX_CopayStatements_TenantId_PaymentDate",
                table: "CopayStatements",
                columns: new[] { "TenantId", "PaymentDate" });

            migrationBuilder.CreateIndex(
                name: "IX_CopayStatements_TenantId_Ref",
                table: "CopayStatements",
                columns: new[] { "TenantId", "Ref" },
                unique: true,
                filter: "[Ref] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CopayStatements");
        }
    }
}
