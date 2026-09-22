using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class AddReminderPayables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReminderPayables",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    Ref = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    AccountTypeId = table.Column<int>(type: "int", nullable: true),
                    ReminderTypeId = table.Column<int>(type: "int", nullable: true),
                    InvoiceTypeId = table.Column<int>(type: "int", nullable: true),
                    CategoryId = table.Column<int>(type: "int", nullable: true),
                    FromAccountId = table.Column<int>(type: "int", nullable: true),
                    ToCategoryId = table.Column<int>(type: "int", nullable: true),
                    ToAccountId = table.Column<int>(type: "int", nullable: true),
                    CreatedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    RemindDay = table.Column<int>(type: "int", nullable: true),
                    RemindDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ReceivedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    LastPaidDate = table.Column<DateOnly>(type: "date", nullable: true),
                    PaidOn = table.Column<DateOnly>(type: "date", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    FrequencyTypeId = table.Column<int>(type: "int", nullable: true),
                    InvoiceType = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    TransTypeId = table.Column<int>(type: "int", nullable: true),
                    Remarks = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    InAlertRoznamcha = table.Column<bool>(type: "bit", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReminderPayables", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReminderPayables_AccountType",
                        column: x => x.AccountTypeId,
                        principalTable: "AppLookupValues",
                        principalColumn: "LookupValueId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReminderPayables_AccountsChartAccounts_FromAccountId",
                        column: x => x.FromAccountId,
                        principalTable: "AccountsChartAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReminderPayables_AccountsChartAccounts_ToAccountId",
                        column: x => x.ToAccountId,
                        principalTable: "AccountsChartAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReminderPayables_AccountsTransTypes_TransTypeId",
                        column: x => x.TransTypeId,
                        principalTable: "AccountsTransTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReminderPayables_Category",
                        column: x => x.CategoryId,
                        principalTable: "AccountsCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReminderPayables_FrequencyTypes_FrequencyTypeId",
                        column: x => x.FrequencyTypeId,
                        principalSchema: "PlatformTypes",
                        principalTable: "FrequencyTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReminderPayables_InvoiceType",
                        column: x => x.InvoiceTypeId,
                        principalTable: "AppLookupValues",
                        principalColumn: "LookupValueId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReminderPayables_ReminderType",
                        column: x => x.ReminderTypeId,
                        principalTable: "AppLookupValues",
                        principalColumn: "LookupValueId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReminderPayables_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReminderPayables_ToCategory",
                        column: x => x.ToCategoryId,
                        principalTable: "AccountsCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReminderPayables_AccountTypeId",
                table: "ReminderPayables",
                column: "AccountTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_ReminderPayables_CategoryId",
                table: "ReminderPayables",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_ReminderPayables_FrequencyTypeId",
                table: "ReminderPayables",
                column: "FrequencyTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_ReminderPayables_FromAccountId",
                table: "ReminderPayables",
                column: "FromAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_ReminderPayables_InvoiceTypeId",
                table: "ReminderPayables",
                column: "InvoiceTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_ReminderPayables_ReminderTypeId",
                table: "ReminderPayables",
                column: "ReminderTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_ReminderPayables_TenantId_DueDate",
                table: "ReminderPayables",
                columns: new[] { "TenantId", "DueDate" });

            migrationBuilder.CreateIndex(
                name: "IX_ReminderPayables_TenantId_Ref",
                table: "ReminderPayables",
                columns: new[] { "TenantId", "Ref" },
                unique: true,
                filter: "[Ref] IS NOT NULL AND [Ref] <> N''");

            migrationBuilder.CreateIndex(
                name: "IX_ReminderPayables_ToAccountId",
                table: "ReminderPayables",
                column: "ToAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_ReminderPayables_ToCategoryId",
                table: "ReminderPayables",
                column: "ToCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_ReminderPayables_TransTypeId",
                table: "ReminderPayables",
                column: "TransTypeId");

            migrationBuilder.Sql(
                """
                CREATE OR ALTER PROCEDURE dbo.usp_Reminders_PayableList
                    @TenantId INT
                AS
                BEGIN
                    SET NOCOUNT ON;

                    SELECT
                        r.Id,
                        r.Ref,
                        r.AccountTypeId,
                        accountType.DisplayText AS AccountType,
                        r.ReminderTypeId,
                        reminderType.DisplayText AS ReminderType,
                        r.InvoiceTypeId,
                        r.CategoryId,
                        category.Name AS Category,
                        r.FromAccountId,
                        fromAcct.AccountName AS FromAcctName,
                        fromAcct.AccountNumber AS FromAcctNo,
                        r.ToCategoryId,
                        toCategory.Name AS ToCategory,
                        r.ToAccountId,
                        toAcct.AccountName AS ToAcctName,
                        toAcct.AccountNumber AS ToAcctNo,
                        r.CreatedOn,
                        r.DueDate,
                        r.RemindDay,
                        r.RemindDate,
                        r.ReceivedOn,
                        r.LastPaidDate,
                        r.PaidOn,
                        r.Amount,
                        r.FrequencyTypeId,
                        freq.Name AS Frequency,
                        COALESCE(invoiceType.DisplayText, r.InvoiceType) AS InvoiceType,
                        r.TransTypeId,
                        transType.Name AS TransactionType,
                        r.Remarks,
                        r.IsActive,
                        r.InAlertRoznamcha
                    FROM dbo.ReminderPayables r
                    LEFT JOIN dbo.AppLookupValues accountType ON accountType.LookupValueId = r.AccountTypeId
                    LEFT JOIN dbo.AppLookupValues reminderType ON reminderType.LookupValueId = r.ReminderTypeId
                    LEFT JOIN dbo.AppLookupValues invoiceType ON invoiceType.LookupValueId = r.InvoiceTypeId
                    LEFT JOIN dbo.AccountsCategories category ON category.Id = r.CategoryId AND category.TenantId = r.TenantId
                    LEFT JOIN dbo.AccountsChartAccounts fromAcct ON fromAcct.Id = r.FromAccountId AND fromAcct.TenantId = r.TenantId
                    LEFT JOIN dbo.AccountsCategories toCategory ON toCategory.Id = r.ToCategoryId AND toCategory.TenantId = r.TenantId
                    LEFT JOIN dbo.AccountsChartAccounts toAcct ON toAcct.Id = r.ToAccountId AND toAcct.TenantId = r.TenantId
                    LEFT JOIN PlatformTypes.FrequencyTypes freq ON freq.Id = r.FrequencyTypeId AND freq.TenantId = r.TenantId
                    LEFT JOIN dbo.AccountsTransTypes transType ON transType.Id = r.TransTypeId AND (transType.TenantId IS NULL OR transType.TenantId = r.TenantId)
                    WHERE r.TenantId = @TenantId
                    ORDER BY r.Id DESC;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.usp_Reminders_PayableList;");

            migrationBuilder.DropTable(
                name: "ReminderPayables");
        }
    }
}
