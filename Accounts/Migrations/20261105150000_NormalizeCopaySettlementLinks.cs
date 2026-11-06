using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261105150000_NormalizeCopaySettlementLinks")]
public sealed class NormalizeCopaySettlementLinks : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "CopaySettlementLinks",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                TenantId = table.Column<int>(type: "int", nullable: false),
                SourceRoznamchaEntryId = table.Column<long>(type: "bigint", nullable: true),
                SourceCopayStatementId = table.Column<long>(type: "bigint", nullable: true),
                TargetRoznamchaEntryId = table.Column<long>(type: "bigint", nullable: true),
                TargetCopayStatementId = table.Column<long>(type: "bigint", nullable: true),
                TargetBankStatementId = table.Column<long>(type: "bigint", nullable: true),
                LinkType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                SourceReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                TargetReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                StatusId = table.Column<int>(type: "int", nullable: true),
                IsManual = table.Column<bool>(type: "bit", nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                Remarks = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                VoidedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                VoidedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CopaySettlementLinks", x => x.Id);
                table.CheckConstraint("CK_CopaySettlementLinks_Source", "[SourceRoznamchaEntryId] IS NOT NULL OR [SourceCopayStatementId] IS NOT NULL");
                table.CheckConstraint("CK_CopaySettlementLinks_Target", "[TargetRoznamchaEntryId] IS NOT NULL OR [TargetCopayStatementId] IS NOT NULL OR [TargetBankStatementId] IS NOT NULL OR [TargetReference] IS NOT NULL");
                table.ForeignKey("FK_CopaySettlementLinks_Tenants_TenantId", x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_CopaySettlementLinks_SourceRoznamcha", x => x.SourceRoznamchaEntryId, "RoznamchaEntries", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_CopaySettlementLinks_SourceStatement", x => x.SourceCopayStatementId, "CopayStatements", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_CopaySettlementLinks_TargetRoznamcha", x => x.TargetRoznamchaEntryId, "RoznamchaEntries", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_CopaySettlementLinks_TargetStatement", x => x.TargetCopayStatementId, "CopayStatements", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_CopaySettlementLinks_TargetBank", x => x.TargetBankStatementId, "BankStatements", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_CopaySettlementLinks_Status", x => x.StatusId, "Statuses", "Id", principalSchema: "PlatformSettings", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex("IX_CopaySettlementLinks_TenantId_SourceRoznamchaEntryId_IsActive", "CopaySettlementLinks", new[] { "TenantId", "SourceRoznamchaEntryId", "IsActive" });
        migrationBuilder.CreateIndex("IX_CopaySettlementLinks_TenantId_SourceCopayStatementId_IsActive", "CopaySettlementLinks", new[] { "TenantId", "SourceCopayStatementId", "IsActive" });
        migrationBuilder.CreateIndex("IX_CopaySettlementLinks_TenantId_TargetRoznamchaEntryId_IsActive", "CopaySettlementLinks", new[] { "TenantId", "TargetRoznamchaEntryId", "IsActive" });
        migrationBuilder.CreateIndex("IX_CopaySettlementLinks_TenantId_TargetCopayStatementId_IsActive", "CopaySettlementLinks", new[] { "TenantId", "TargetCopayStatementId", "IsActive" });
        migrationBuilder.CreateIndex("IX_CopaySettlementLinks_TenantId_TargetBankStatementId_IsActive", "CopaySettlementLinks", new[] { "TenantId", "TargetBankStatementId", "IsActive" });
        migrationBuilder.CreateIndex("IX_CopaySettlementLinks_StatusId", "CopaySettlementLinks", "StatusId");
        migrationBuilder.CreateIndex("IX_CopayStatements_AcctId", "CopayStatements", "AcctId");

        migrationBuilder.AddForeignKey("FK_CopayStatements_Tenants_TenantId", "CopayStatements", "TenantId", "Tenants", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey("FK_CopayStatements_AccountsChartAccounts_AcctId", "CopayStatements", "AcctId", "AccountsChartAccounts", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey("FK_CopayStatements_RoznamchaEntries_MatchedRoznamchaEntryId", "CopayStatements", "MatchedRoznamchaEntryId", "RoznamchaEntries", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey("FK_CopayStatements_Tenants_TenantId", "CopayStatements");
        migrationBuilder.DropForeignKey("FK_CopayStatements_AccountsChartAccounts_AcctId", "CopayStatements");
        migrationBuilder.DropForeignKey("FK_CopayStatements_RoznamchaEntries_MatchedRoznamchaEntryId", "CopayStatements");
        migrationBuilder.DropIndex("IX_CopayStatements_AcctId", "CopayStatements");
        migrationBuilder.DropTable("CopaySettlementLinks");
    }
}
