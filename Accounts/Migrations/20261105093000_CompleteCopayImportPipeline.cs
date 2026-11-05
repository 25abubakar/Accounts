using System;
using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

/// <summary>
/// Adds only the tenant-aware Copay import/configuration schema. This migration is
/// intentionally isolated from unrelated Billing/Claims model drift.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20261105093000_CompleteCopayImportPipeline")]
public sealed class CompleteCopayImportPipeline : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(name: "ImportBatchId", table: "CopayStatements", type: "bigint", nullable: true);
        migrationBuilder.AddColumn<int>(name: "LegacyId", table: "CopayStatements", type: "int", nullable: true);
        migrationBuilder.AddColumn<int>(name: "LegacyOrganizationId", table: "CopayStatements", type: "int", nullable: true);
        migrationBuilder.AddColumn<string>(name: "SourceFingerprint", table: "CopayStatements", type: "nvarchar(128)", maxLength: 128, nullable: true);
        migrationBuilder.AddColumn<int>(name: "SourceRowNumber", table: "CopayStatements", type: "int", nullable: true);

        migrationBuilder.AddColumn<int>(name: "CopayBankMatchWindowDays", table: "AccountsModuleSettings", type: "int", nullable: false, defaultValue: 6);
        migrationBuilder.AddColumn<int>(name: "CopayCategoryId", table: "AccountsModuleSettings", type: "int", nullable: true);
        migrationBuilder.AddColumn<int>(name: "CopayCurrencyId", table: "AccountsModuleSettings", type: "int", nullable: true);
        migrationBuilder.AddColumn<int>(name: "CopayHoldingAccountId", table: "AccountsModuleSettings", type: "int", nullable: true);
        migrationBuilder.AddColumn<int>(name: "CopayMerchantAccountId", table: "AccountsModuleSettings", type: "int", nullable: true);
        migrationBuilder.AddColumn<int>(name: "CopayMerchantClearingAccountId", table: "AccountsModuleSettings", type: "int", nullable: true);
        migrationBuilder.AddColumn<int>(name: "CopayMerchantMatchWindowDays", table: "AccountsModuleSettings", type: "int", nullable: false, defaultValue: 14);
        migrationBuilder.AddColumn<int>(name: "CopayReceiptTypeId", table: "AccountsModuleSettings", type: "int", nullable: true);

        migrationBuilder.CreateTable(
            name: "CopayImportBatches",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                TenantId = table.Column<int>(type: "int", nullable: false),
                Stage = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                FileHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                TotalRows = table.Column<int>(type: "int", nullable: false),
                ImportedRows = table.Column<int>(type: "int", nullable: false),
                DuplicateRows = table.Column<int>(type: "int", nullable: false),
                InvalidRows = table.Column<int>(type: "int", nullable: false),
                ErrorSummary = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                CompletedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CopayImportBatches", x => x.Id);
                table.ForeignKey("FK_CopayImportBatches_Tenants_TenantId", x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "CopayImportRows",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                TenantId = table.Column<int>(type: "int", nullable: false),
                ImportBatchId = table.Column<long>(type: "bigint", nullable: false),
                SourceRowNumber = table.Column<int>(type: "int", nullable: false),
                Fingerprint = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                Result = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                Message = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                RoznamchaEntryId = table.Column<long>(type: "bigint", nullable: true),
                CopayStatementId = table.Column<long>(type: "bigint", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CopayImportRows", x => x.Id);
                table.ForeignKey("FK_CopayImportRows_CopayImportBatches_ImportBatchId", x => x.ImportBatchId, "CopayImportBatches", "Id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_CopayImportRows_CopayStatements_CopayStatementId", x => x.CopayStatementId, "CopayStatements", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_CopayImportRows_RoznamchaEntries_RoznamchaEntryId", x => x.RoznamchaEntryId, "RoznamchaEntries", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex("IX_CopayImportBatches_TenantId_CreatedOnUtc", "CopayImportBatches", new[] { "TenantId", "CreatedOnUtc" });
        migrationBuilder.CreateIndex("IX_CopayImportBatches_TenantId_FileHash_Stage", "CopayImportBatches", new[] { "TenantId", "FileHash", "Stage" }, unique: true);
        migrationBuilder.CreateIndex("IX_CopayImportRows_CopayStatementId", "CopayImportRows", "CopayStatementId");
        migrationBuilder.CreateIndex("IX_CopayImportRows_ImportBatchId", "CopayImportRows", "ImportBatchId");
        migrationBuilder.CreateIndex("IX_CopayImportRows_RoznamchaEntryId", "CopayImportRows", "RoznamchaEntryId");
        migrationBuilder.CreateIndex("IX_CopayImportRows_TenantId_Fingerprint", "CopayImportRows", new[] { "TenantId", "Fingerprint" });
        migrationBuilder.CreateIndex("IX_CopayImportRows_TenantId_ImportBatchId_SourceRowNumber", "CopayImportRows", new[] { "TenantId", "ImportBatchId", "SourceRowNumber" }, unique: true);
        migrationBuilder.CreateIndex("IX_CopayStatements_ImportBatchId", "CopayStatements", "ImportBatchId");
        migrationBuilder.CreateIndex("IX_CopayStatements_TenantId_LegacyId", "CopayStatements", new[] { "TenantId", "LegacyId" }, unique: true, filter: "[LegacyId] IS NOT NULL");
        migrationBuilder.CreateIndex("IX_CopayStatements_TenantId_SourceFingerprint", "CopayStatements", new[] { "TenantId", "SourceFingerprint" });

        migrationBuilder.CreateIndex("IX_AccountsModuleSettings_CopayCategoryId", "AccountsModuleSettings", "CopayCategoryId");
        migrationBuilder.CreateIndex("IX_AccountsModuleSettings_CopayCurrencyId", "AccountsModuleSettings", "CopayCurrencyId");
        migrationBuilder.CreateIndex("IX_AccountsModuleSettings_CopayHoldingAccountId", "AccountsModuleSettings", "CopayHoldingAccountId");
        migrationBuilder.CreateIndex("IX_AccountsModuleSettings_CopayMerchantAccountId", "AccountsModuleSettings", "CopayMerchantAccountId");
        migrationBuilder.CreateIndex("IX_AccountsModuleSettings_CopayMerchantClearingAccountId", "AccountsModuleSettings", "CopayMerchantClearingAccountId");
        migrationBuilder.CreateIndex("IX_AccountsModuleSettings_CopayReceiptTypeId", "AccountsModuleSettings", "CopayReceiptTypeId");

        migrationBuilder.AddForeignKey("FK_AccountsModuleSettings_AccountsCategories_CopayCategoryId", "AccountsModuleSettings", "CopayCategoryId", "AccountsCategories", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey("FK_AccountsModuleSettings_AccountsCurrencies_CopayCurrencyId", "AccountsModuleSettings", "CopayCurrencyId", "AccountsCurrencies", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey("FK_AccountsModuleSettings_AccountsChartAccounts_CopayHoldingAccountId", "AccountsModuleSettings", "CopayHoldingAccountId", "AccountsChartAccounts", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey("FK_AccountsModuleSettings_AccountsChartAccounts_CopayMerchantAccountId", "AccountsModuleSettings", "CopayMerchantAccountId", "AccountsChartAccounts", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey("FK_AccountsModuleSettings_AccountsChartAccounts_CopayMerchantClearingAccountId", "AccountsModuleSettings", "CopayMerchantClearingAccountId", "AccountsChartAccounts", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey("FK_AccountsModuleSettings_AccountsRoznamchaTypes_CopayReceiptTypeId", "AccountsModuleSettings", "CopayReceiptTypeId", "AccountsRoznamchaTypes", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey("FK_CopayStatements_CopayImportBatches_ImportBatchId", "CopayStatements", "ImportBatchId", "CopayImportBatches", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey("FK_CopayStatements_CopayImportBatches_ImportBatchId", "CopayStatements");
        migrationBuilder.DropForeignKey("FK_AccountsModuleSettings_AccountsCategories_CopayCategoryId", "AccountsModuleSettings");
        migrationBuilder.DropForeignKey("FK_AccountsModuleSettings_AccountsCurrencies_CopayCurrencyId", "AccountsModuleSettings");
        migrationBuilder.DropForeignKey("FK_AccountsModuleSettings_AccountsChartAccounts_CopayHoldingAccountId", "AccountsModuleSettings");
        migrationBuilder.DropForeignKey("FK_AccountsModuleSettings_AccountsChartAccounts_CopayMerchantAccountId", "AccountsModuleSettings");
        migrationBuilder.DropForeignKey("FK_AccountsModuleSettings_AccountsChartAccounts_CopayMerchantClearingAccountId", "AccountsModuleSettings");
        migrationBuilder.DropForeignKey("FK_AccountsModuleSettings_AccountsRoznamchaTypes_CopayReceiptTypeId", "AccountsModuleSettings");

        migrationBuilder.DropTable("CopayImportRows");
        migrationBuilder.DropTable("CopayImportBatches");

        migrationBuilder.DropIndex("IX_AccountsModuleSettings_CopayCategoryId", "AccountsModuleSettings");
        migrationBuilder.DropIndex("IX_AccountsModuleSettings_CopayCurrencyId", "AccountsModuleSettings");
        migrationBuilder.DropIndex("IX_AccountsModuleSettings_CopayHoldingAccountId", "AccountsModuleSettings");
        migrationBuilder.DropIndex("IX_AccountsModuleSettings_CopayMerchantAccountId", "AccountsModuleSettings");
        migrationBuilder.DropIndex("IX_AccountsModuleSettings_CopayMerchantClearingAccountId", "AccountsModuleSettings");
        migrationBuilder.DropIndex("IX_AccountsModuleSettings_CopayReceiptTypeId", "AccountsModuleSettings");
        migrationBuilder.DropIndex("IX_CopayStatements_TenantId_LegacyId", "CopayStatements");
        migrationBuilder.DropIndex("IX_CopayStatements_TenantId_SourceFingerprint", "CopayStatements");

        migrationBuilder.DropColumn("ImportBatchId", "CopayStatements");
        migrationBuilder.DropColumn("LegacyId", "CopayStatements");
        migrationBuilder.DropColumn("LegacyOrganizationId", "CopayStatements");
        migrationBuilder.DropColumn("SourceFingerprint", "CopayStatements");
        migrationBuilder.DropColumn("SourceRowNumber", "CopayStatements");
        migrationBuilder.DropColumn("CopayBankMatchWindowDays", "AccountsModuleSettings");
        migrationBuilder.DropColumn("CopayCategoryId", "AccountsModuleSettings");
        migrationBuilder.DropColumn("CopayCurrencyId", "AccountsModuleSettings");
        migrationBuilder.DropColumn("CopayHoldingAccountId", "AccountsModuleSettings");
        migrationBuilder.DropColumn("CopayMerchantAccountId", "AccountsModuleSettings");
        migrationBuilder.DropColumn("CopayMerchantClearingAccountId", "AccountsModuleSettings");
        migrationBuilder.DropColumn("CopayMerchantMatchWindowDays", "AccountsModuleSettings");
        migrationBuilder.DropColumn("CopayReceiptTypeId", "AccountsModuleSettings");
    }
}
