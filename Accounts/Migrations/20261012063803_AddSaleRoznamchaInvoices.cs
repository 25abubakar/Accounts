using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class AddSaleRoznamchaInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "InvoiceId",
                table: "SaleRoznamchaDailySales",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SaleRoznamchaInvoices",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    ReceiptNumber = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    OrderId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SaleStatusId = table.Column<int>(type: "int", nullable: false),
                    SaleCurrencyId = table.Column<int>(type: "int", nullable: false),
                    PayChargeEffectId = table.Column<int>(type: "int", nullable: false),
                    PayCharges = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DeliveryFee = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    OtherAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PromotionAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    RtoChargeAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ItemsSubtotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TotalAdditions = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TotalDeductions = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    NetTotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TransactionTypeId = table.Column<int>(type: "int", nullable: true),
                    PaidStatusId = table.Column<int>(type: "int", nullable: true),
                    ToAccountId = table.Column<int>(type: "int", nullable: true),
                    InterProReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    OrderReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SaleDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaleRoznamchaInvoices", x => x.Id);
                    table.UniqueConstraint("AK_SaleRoznamchaInvoices_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_SaleRoznamchaInvoices_AccountsCurrencies_SaleCurrencyId",
                        column: x => x.SaleCurrencyId,
                        principalTable: "AccountsCurrencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SaleRoznamchaInvoices_SaleRoznamchaAmountEffects_TenantId_PayChargeEffectId",
                        columns: x => new { x.TenantId, x.PayChargeEffectId },
                        principalTable: "SaleRoznamchaAmountEffects",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SaleRoznamchaInvoices_SaleRoznamchaSaleStatuses_TenantId_SaleStatusId",
                        columns: x => new { x.TenantId, x.SaleStatusId },
                        principalTable: "SaleRoznamchaSaleStatuses",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SaleRoznamchaInvoices_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SaleRoznamchaInvoiceCharges",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    InvoiceId = table.Column<long>(type: "bigint", nullable: false),
                    ChargeLabelId = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaleRoznamchaInvoiceCharges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SaleRoznamchaInvoiceCharges_SaleRoznamchaChargeLabels_TenantId_ChargeLabelId",
                        columns: x => new { x.TenantId, x.ChargeLabelId },
                        principalTable: "SaleRoznamchaChargeLabels",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SaleRoznamchaInvoiceCharges_SaleRoznamchaInvoices_TenantId_InvoiceId",
                        columns: x => new { x.TenantId, x.InvoiceId },
                        principalTable: "SaleRoznamchaInvoices",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SaleRoznamchaInvoiceCharges_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SaleRoznamchaInvoiceTransactionDates",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    InvoiceId = table.Column<long>(type: "bigint", nullable: false),
                    DateLabelId = table.Column<int>(type: "int", nullable: false),
                    Value = table.Column<DateOnly>(type: "date", nullable: false),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaleRoznamchaInvoiceTransactionDates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SaleRoznamchaInvoiceTransactionDates_SaleRoznamchaInvoices_TenantId_InvoiceId",
                        columns: x => new { x.TenantId, x.InvoiceId },
                        principalTable: "SaleRoznamchaInvoices",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SaleRoznamchaInvoiceTransactionDates_SaleRoznamchaTransactionDateLabels_TenantId_DateLabelId",
                        columns: x => new { x.TenantId, x.DateLabelId },
                        principalTable: "SaleRoznamchaTransactionDateLabels",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SaleRoznamchaInvoiceTransactionDates_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            // Preserve every existing one-line sale as its own receipt. New receipts may contain
            // any number of product lines, while historic stock movements remain unchanged.
            migrationBuilder.Sql("""
                INSERT INTO dbo.SaleRoznamchaInvoices
                (
                    TenantId, ReceiptNumber, OrderId, SaleStatusId, SaleCurrencyId,
                    PayChargeEffectId, PayCharges, DeliveryFee, OtherAmount, PromotionAmount,
                    RtoChargeAmount, ItemsSubtotal, TotalAdditions, TotalDeductions, NetTotal,
                    TransactionTypeId, PaidStatusId, ToAccountId, InterProReference,
                    OrderReference, SaleDate, Description, CreatedByUserId, CreatedOnUtc,
                    UpdatedByUserId, UpdatedOnUtc
                )
                SELECT
                    sale.TenantId, CONCAT(N'SR-L-', sale.Id), sale.OrderId, sale.SaleStatusId,
                    sale.SaleCurrencyId, sale.PayChargeEffectId, sale.PayCharges, sale.DeliveryFee,
                    sale.OtherAmount, sale.PromotionAmount, sale.RtoChargeAmount,
                    sale.BaseSaleAmount, sale.TotalAdditions, sale.TotalDeductions,
                    sale.NetSaleTotal, sale.TransactionTypeId, sale.PaidStatusId, sale.ToAccountId,
                    sale.InterProReference, sale.OrderReference, sale.SaleDate, sale.Description,
                    sale.CreatedByUserId, sale.CreatedOnUtc, NULL, NULL
                FROM dbo.SaleRoznamchaDailySales sale;

                UPDATE sale
                SET InvoiceId = invoice.Id
                FROM dbo.SaleRoznamchaDailySales sale
                INNER JOIN dbo.SaleRoznamchaInvoices invoice
                    ON invoice.TenantId = sale.TenantId
                   AND invoice.ReceiptNumber = CONCAT(N'SR-L-', sale.Id);

                INSERT INTO dbo.SaleRoznamchaInvoiceCharges
                    (TenantId, InvoiceId, ChargeLabelId, Amount, CreatedOnUtc)
                SELECT charge.TenantId, sale.InvoiceId, charge.ChargeLabelId,
                       charge.Amount, charge.CreatedOnUtc
                FROM dbo.SaleRoznamchaDailySaleCharges charge
                INNER JOIN dbo.SaleRoznamchaDailySales sale
                    ON sale.TenantId = charge.TenantId AND sale.Id = charge.DailySaleId;

                INSERT INTO dbo.SaleRoznamchaInvoiceTransactionDates
                    (TenantId, InvoiceId, DateLabelId, Value, CreatedOnUtc)
                SELECT txDate.TenantId, sale.InvoiceId, txDate.DateLabelId,
                       txDate.Value, txDate.CreatedOnUtc
                FROM dbo.SaleRoznamchaDailySaleTransactionDates txDate
                INNER JOIN dbo.SaleRoznamchaDailySales sale
                    ON sale.TenantId = txDate.TenantId AND sale.Id = txDate.DailySaleId;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_SaleRoznamchaDailySales_TenantId_InvoiceId",
                table: "SaleRoznamchaDailySales",
                columns: new[] { "TenantId", "InvoiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_SaleRoznamchaInvoiceCharges_TenantId_ChargeLabelId",
                table: "SaleRoznamchaInvoiceCharges",
                columns: new[] { "TenantId", "ChargeLabelId" });

            migrationBuilder.CreateIndex(
                name: "IX_SaleRoznamchaInvoiceCharges_TenantId_InvoiceId_ChargeLabelId",
                table: "SaleRoznamchaInvoiceCharges",
                columns: new[] { "TenantId", "InvoiceId", "ChargeLabelId" });

            migrationBuilder.CreateIndex(
                name: "IX_SaleRoznamchaInvoices_SaleCurrencyId",
                table: "SaleRoznamchaInvoices",
                column: "SaleCurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleRoznamchaInvoices_TenantId_CreatedOnUtc",
                table: "SaleRoznamchaInvoices",
                columns: new[] { "TenantId", "CreatedOnUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SaleRoznamchaInvoices_TenantId_PayChargeEffectId",
                table: "SaleRoznamchaInvoices",
                columns: new[] { "TenantId", "PayChargeEffectId" });

            migrationBuilder.CreateIndex(
                name: "IX_SaleRoznamchaInvoices_TenantId_ReceiptNumber",
                table: "SaleRoznamchaInvoices",
                columns: new[] { "TenantId", "ReceiptNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SaleRoznamchaInvoices_TenantId_SaleStatusId",
                table: "SaleRoznamchaInvoices",
                columns: new[] { "TenantId", "SaleStatusId" });

            migrationBuilder.CreateIndex(
                name: "IX_SaleRoznamchaInvoiceTransactionDates_TenantId_DateLabelId",
                table: "SaleRoznamchaInvoiceTransactionDates",
                columns: new[] { "TenantId", "DateLabelId" });

            migrationBuilder.CreateIndex(
                name: "IX_SaleRoznamchaInvoiceTransactionDates_TenantId_InvoiceId_DateLabelId",
                table: "SaleRoznamchaInvoiceTransactionDates",
                columns: new[] { "TenantId", "InvoiceId", "DateLabelId" });

            migrationBuilder.AddForeignKey(
                name: "FK_SaleRoznamchaDailySales_SaleRoznamchaInvoices_TenantId_InvoiceId",
                table: "SaleRoznamchaDailySales",
                columns: new[] { "TenantId", "InvoiceId" },
                principalTable: "SaleRoznamchaInvoices",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SaleRoznamchaDailySales_SaleRoznamchaInvoices_TenantId_InvoiceId",
                table: "SaleRoznamchaDailySales");

            migrationBuilder.DropTable(
                name: "SaleRoznamchaInvoiceCharges");

            migrationBuilder.DropTable(
                name: "SaleRoznamchaInvoiceTransactionDates");

            migrationBuilder.DropTable(
                name: "SaleRoznamchaInvoices");

            migrationBuilder.DropIndex(
                name: "IX_SaleRoznamchaDailySales_TenantId_InvoiceId",
                table: "SaleRoznamchaDailySales");

            migrationBuilder.DropColumn(
                name: "InvoiceId",
                table: "SaleRoznamchaDailySales");
        }
    }
}
