using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class CompleteAccountsBackend : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsFromLibrary",
                table: "RoznamchaEntries",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Attachment",
                table: "ReminderReceivables",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CurrencyId",
                table: "ReminderReceivables",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsInactive",
                table: "ReminderReceivables",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsNotRoznamcha",
                table: "ReminderReceivables",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "StatusId",
                table: "ReminderReceivables",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Attachment",
                table: "ReminderPayables",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CurrencyId",
                table: "ReminderPayables",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsInactive",
                table: "ReminderPayables",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsNotRoznamcha",
                table: "ReminderPayables",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "StatusId",
                table: "ReminderPayables",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Attachment",
                table: "BankStatements",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ColorId",
                table: "BankStatements",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DateFormat",
                table: "BankStatements",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsManual",
                table: "BankStatements",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsReversal",
                table: "BankStatements",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsSettled",
                table: "BankStatements",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "PostingDate",
                table: "BankStatements",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferenceNumber",
                table: "BankStatements",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Remarks",
                table: "BankStatements",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StatusId",
                table: "BankStatements",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TransactionReferenceNumber",
                table: "BankStatements",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedByUserId",
                table: "BankStatements",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedOnUtc",
                table: "BankStatements",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "YearId",
                table: "BankStatements",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DocumentReference",
                table: "AccountsEntryDocuments",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Remarks",
                table: "AccountsEntryDocuments",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AccountCode",
                table: "AccountsChartAccounts",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AccountLimit",
                table: "AccountsChartAccounts",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "AccountReference",
                table: "AccountsChartAccounts",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Address",
                table: "AccountsChartAccounts",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Attachment",
                table: "AccountsChartAccounts",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "BalanceAmount",
                table: "AccountsChartAccounts",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "BankAccountNumber",
                table: "AccountsChartAccounts",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "BudgetAmount",
                table: "AccountsChartAccounts",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "CnicNtn",
                table: "AccountsChartAccounts",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedByUserId",
                table: "AccountsChartAccounts",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedOnUtc",
                table: "AccountsChartAccounts",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "SYSUTCDATETIME()");

            migrationBuilder.AddColumn<decimal>(
                name: "Credit",
                table: "AccountsChartAccounts",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "Debit",
                table: "AccountsChartAccounts",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "AccountsChartAccounts",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DesignationId",
                table: "AccountsChartAccounts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "AccountsChartAccounts",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FullName",
                table: "AccountsChartAccounts",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsInventory",
                table: "AccountsChartAccounts",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsStaff",
                table: "AccountsChartAccounts",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsStatement",
                table: "AccountsChartAccounts",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "ParentId",
                table: "AccountsChartAccounts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PersonId",
                table: "AccountsChartAccounts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Phone",
                table: "AccountsChartAccounts",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StatusId",
                table: "AccountsChartAccounts",
                type: "int",
                nullable: false,
                defaultValue: 11);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedByUserId",
                table: "AccountsChartAccounts",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedOnUtc",
                table: "AccountsChartAccounts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "UsedAmount",
                table: "AccountsChartAccounts",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "BalanceAmount",
                table: "AccountsCategories",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "BudgetAmount",
                table: "AccountsCategories",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "CategoryTypeId",
                table: "AccountsCategories",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedByUserId",
                table: "AccountsCategories",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedOnUtc",
                table: "AccountsCategories",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "SYSUTCDATETIME()");

            migrationBuilder.AddColumn<bool>(
                name: "IsNotInReport",
                table: "AccountsCategories",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Number",
                table: "AccountsCategories",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferenceNumber",
                table: "AccountsCategories",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedByUserId",
                table: "AccountsCategories",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedOnUtc",
                table: "AccountsCategories",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "UsedAmount",
                table: "AccountsCategories",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "AccountsCategoryTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountsCategoryTypes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccountsCategoryTypes_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AccountsProjectCostRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    TypeId = table.Column<int>(type: "int", nullable: true),
                    CategoryId = table.Column<int>(type: "int", nullable: true),
                    AccountId = table.Column<int>(type: "int", nullable: true),
                    EntryId = table.Column<long>(type: "bigint", nullable: true),
                    PercentageValue = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountsProjectCostRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccountsProjectCostRules_AccountsCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "AccountsCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccountsProjectCostRules_AccountsChartAccounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "AccountsChartAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccountsProjectCostRules_RoznamchaEntries_EntryId",
                        column: x => x.EntryId,
                        principalTable: "RoznamchaEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccountsProjectCostRules_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AccountsProjects",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountsProjects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccountsProjects_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AccountsReportFilters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    CategoryId = table.Column<int>(type: "int", nullable: true),
                    AccountId = table.Column<int>(type: "int", nullable: true),
                    TypeId = table.Column<int>(type: "int", nullable: true),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountsReportFilters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccountsReportFilters_AccountsCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "AccountsCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccountsReportFilters_AccountsChartAccounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "AccountsChartAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccountsReportFilters_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AccountsTaxTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: true),
                    Code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    DefaultRate = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountsTaxTypes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccountsTaxTypes_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });



            migrationBuilder.CreateTable(
                name: "AccountsReportFilterAccounts",
                columns: table => new
                {
                    ReportFilterId = table.Column<int>(type: "int", nullable: false),
                    AccountId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountsReportFilterAccounts", x => new { x.ReportFilterId, x.AccountId });
                    table.ForeignKey(
                        name: "FK_AccountsReportFilterAccounts_AccountsChartAccounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "AccountsChartAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccountsReportFilterAccounts_AccountsReportFilters_ReportFilterId",
                        column: x => x.ReportFilterId,
                        principalTable: "AccountsReportFilters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AccountsReportFilterCategories",
                columns: table => new
                {
                    ReportFilterId = table.Column<int>(type: "int", nullable: false),
                    CategoryId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountsReportFilterCategories", x => new { x.ReportFilterId, x.CategoryId });
                    table.ForeignKey(
                        name: "FK_AccountsReportFilterCategories_AccountsCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "AccountsCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccountsReportFilterCategories_AccountsReportFilters_ReportFilterId",
                        column: x => x.ReportFilterId,
                        principalTable: "AccountsReportFilters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AccountsReportFilterSubAccounts",
                columns: table => new
                {
                    ReportFilterId = table.Column<int>(type: "int", nullable: false),
                    SubAccountId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountsReportFilterSubAccounts", x => new { x.ReportFilterId, x.SubAccountId });
                    table.ForeignKey(
                        name: "FK_AccountsReportFilterSubAccounts_AccountsChartAccounts_SubAccountId",
                        column: x => x.SubAccountId,
                        principalTable: "AccountsChartAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccountsReportFilterSubAccounts_AccountsReportFilters_ReportFilterId",
                        column: x => x.ReportFilterId,
                        principalTable: "AccountsReportFilters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });



            migrationBuilder.CreateIndex(
                name: "IX_AccountsChartAccounts_ParentId",
                table: "AccountsChartAccounts",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountsChartAccounts_TenantId_AccountCode",
                table: "AccountsChartAccounts",
                columns: new[] { "TenantId", "AccountCode" },
                unique: true,
                filter: "[AccountCode] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AccountsChartAccounts_TenantId_AccountReference",
                table: "AccountsChartAccounts",
                columns: new[] { "TenantId", "AccountReference" },
                unique: true,
                filter: "[AccountReference] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AccountsChartAccounts_TenantId_ParentId",
                table: "AccountsChartAccounts",
                columns: new[] { "TenantId", "ParentId" });

            migrationBuilder.CreateIndex(
                name: "IX_AccountsCategories_CategoryTypeId",
                table: "AccountsCategories",
                column: "CategoryTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountsCategories_TenantId_Name",
                table: "AccountsCategories",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountsCategoryTypes_TenantId_Name",
                table: "AccountsCategoryTypes",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountsProjectCostRules_AccountId",
                table: "AccountsProjectCostRules",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountsProjectCostRules_CategoryId",
                table: "AccountsProjectCostRules",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountsProjectCostRules_EntryId",
                table: "AccountsProjectCostRules",
                column: "EntryId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountsProjectCostRules_TenantId_Name",
                table: "AccountsProjectCostRules",
                columns: new[] { "TenantId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_AccountsProjects_TenantId_Name",
                table: "AccountsProjects",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountsReportFilterAccounts_AccountId",
                table: "AccountsReportFilterAccounts",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountsReportFilterCategories_CategoryId",
                table: "AccountsReportFilterCategories",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountsReportFilters_AccountId",
                table: "AccountsReportFilters",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountsReportFilters_CategoryId",
                table: "AccountsReportFilters",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountsReportFilters_TenantId_Name",
                table: "AccountsReportFilters",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountsReportFilterSubAccounts_SubAccountId",
                table: "AccountsReportFilterSubAccounts",
                column: "SubAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountsTaxTypes_TenantId_Code",
                table: "AccountsTaxTypes",
                columns: new[] { "TenantId", "Code" },
                unique: true,
                filter: "[TenantId] IS NOT NULL");









            migrationBuilder.AddForeignKey(
                name: "FK_AccountsCategories_AccountsCategoryTypes_CategoryTypeId",
                table: "AccountsCategories",
                column: "CategoryTypeId",
                principalTable: "AccountsCategoryTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AccountsChartAccounts_AccountsChartAccounts_ParentId",
                table: "AccountsChartAccounts",
                column: "ParentId",
                principalTable: "AccountsChartAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql(
                """
                INSERT INTO dbo.AccountsRoznamchaTypes (TenantId, Code, Name, IsActive)
                SELECT NULL, seed.Code, seed.Name, 1
                FROM (VALUES (N'PAYMENT', N'Payment'), (N'RECEIPT', N'Receipt')) seed(Code, Name)
                WHERE NOT EXISTS (
                    SELECT 1 FROM dbo.AccountsRoznamchaTypes existing
                    WHERE existing.Code = seed.Code AND existing.TenantId IS NULL
                );

                INSERT INTO dbo.AccountsTransModes (TenantId, Code, Name, IsActive)
                SELECT NULL, seed.Code, seed.Name, 1
                FROM (VALUES
                    (N'CASH', N'Cash'), (N'BANK', N'Bank'), (N'CHEQUE', N'Cheque'), (N'ONLINE', N'Online Transfer')
                ) seed(Code, Name)
                WHERE NOT EXISTS (
                    SELECT 1 FROM dbo.AccountsTransModes existing
                    WHERE existing.Code = seed.Code AND existing.TenantId IS NULL
                );

                INSERT INTO dbo.AccountsCurrencies (TenantId, Code, Name, IsActive)
                SELECT NULL, seed.Code, seed.Name, 1
                FROM (VALUES (N'PKR', N'Pakistani Rupee'), (N'USD', N'US Dollar')) seed(Code, Name)
                WHERE NOT EXISTS (
                    SELECT 1 FROM dbo.AccountsCurrencies existing
                    WHERE existing.Code = seed.Code AND existing.TenantId IS NULL
                );

                INSERT INTO dbo.AccountsEntryStatuses (TenantId, Code, Name, ColorCode, FontColor, IsActive)
                SELECT NULL, seed.Code, seed.Name, seed.ColorCode, seed.FontColor, 1
                FROM (VALUES
                    (N'DRAFT', N'Draft', N'#64748b', N'#ffffff'),
                    (N'APPROVED', N'Approved', N'#10b981', N'#ffffff'),
                    (N'SETTLED', N'Settled', N'#0284c7', N'#ffffff')
                ) seed(Code, Name, ColorCode, FontColor)
                WHERE NOT EXISTS (
                    SELECT 1 FROM dbo.AccountsEntryStatuses existing
                    WHERE existing.Code = seed.Code AND existing.TenantId IS NULL
                );

                INSERT INTO dbo.AccountsTaxTypes (TenantId, Code, Name, DefaultRate, IsActive)
                SELECT NULL, seed.Code, seed.Name, seed.DefaultRate, 1
                FROM (VALUES
                    (N'NONE', N'No Tax', CAST(0 AS decimal(18,4))),
                    (N'WHT', N'Withholding Tax', CAST(0 AS decimal(18,4))),
                    (N'SALES', N'Sales Tax', CAST(0 AS decimal(18,4)))
                ) seed(Code, Name, DefaultRate)
                WHERE NOT EXISTS (
                    SELECT 1 FROM dbo.AccountsTaxTypes existing
                    WHERE existing.Code = seed.Code AND existing.TenantId IS NULL
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AccountsCategories_AccountsCategoryTypes_CategoryTypeId",
                table: "AccountsCategories");

            migrationBuilder.DropForeignKey(
                name: "FK_AccountsChartAccounts_AccountsChartAccounts_ParentId",
                table: "AccountsChartAccounts");

            migrationBuilder.DropTable(
                name: "AccountsCategoryTypes");

            migrationBuilder.DropTable(
                name: "AccountsProjectCostRules");

            migrationBuilder.DropTable(
                name: "AccountsProjects");

            migrationBuilder.DropTable(
                name: "AccountsReportFilterAccounts");

            migrationBuilder.DropTable(
                name: "AccountsReportFilterCategories");

            migrationBuilder.DropTable(
                name: "AccountsReportFilterSubAccounts");

            migrationBuilder.DropTable(
                name: "AccountsTaxTypes");



            migrationBuilder.DropTable(
                name: "AccountsReportFilters");



            migrationBuilder.DropIndex(
                name: "IX_AccountsChartAccounts_ParentId",
                table: "AccountsChartAccounts");

            migrationBuilder.DropIndex(
                name: "IX_AccountsChartAccounts_TenantId_AccountCode",
                table: "AccountsChartAccounts");

            migrationBuilder.DropIndex(
                name: "IX_AccountsChartAccounts_TenantId_AccountReference",
                table: "AccountsChartAccounts");

            migrationBuilder.DropIndex(
                name: "IX_AccountsChartAccounts_TenantId_ParentId",
                table: "AccountsChartAccounts");

            migrationBuilder.DropIndex(
                name: "IX_AccountsCategories_CategoryTypeId",
                table: "AccountsCategories");

            migrationBuilder.DropIndex(
                name: "IX_AccountsCategories_TenantId_Name",
                table: "AccountsCategories");

            migrationBuilder.DropColumn(
                name: "IsFromLibrary",
                table: "RoznamchaEntries");

            migrationBuilder.DropColumn(
                name: "Attachment",
                table: "ReminderReceivables");

            migrationBuilder.DropColumn(
                name: "CurrencyId",
                table: "ReminderReceivables");

            migrationBuilder.DropColumn(
                name: "IsInactive",
                table: "ReminderReceivables");

            migrationBuilder.DropColumn(
                name: "IsNotRoznamcha",
                table: "ReminderReceivables");

            migrationBuilder.DropColumn(
                name: "StatusId",
                table: "ReminderReceivables");

            migrationBuilder.DropColumn(
                name: "Attachment",
                table: "ReminderPayables");

            migrationBuilder.DropColumn(
                name: "CurrencyId",
                table: "ReminderPayables");

            migrationBuilder.DropColumn(
                name: "IsInactive",
                table: "ReminderPayables");

            migrationBuilder.DropColumn(
                name: "IsNotRoznamcha",
                table: "ReminderPayables");

            migrationBuilder.DropColumn(
                name: "StatusId",
                table: "ReminderPayables");

            migrationBuilder.DropColumn(
                name: "Attachment",
                table: "BankStatements");

            migrationBuilder.DropColumn(
                name: "ColorId",
                table: "BankStatements");

            migrationBuilder.DropColumn(
                name: "DateFormat",
                table: "BankStatements");

            migrationBuilder.DropColumn(
                name: "IsManual",
                table: "BankStatements");

            migrationBuilder.DropColumn(
                name: "IsReversal",
                table: "BankStatements");

            migrationBuilder.DropColumn(
                name: "IsSettled",
                table: "BankStatements");

            migrationBuilder.DropColumn(
                name: "PostingDate",
                table: "BankStatements");

            migrationBuilder.DropColumn(
                name: "ReferenceNumber",
                table: "BankStatements");

            migrationBuilder.DropColumn(
                name: "Remarks",
                table: "BankStatements");

            migrationBuilder.DropColumn(
                name: "StatusId",
                table: "BankStatements");

            migrationBuilder.DropColumn(
                name: "TransactionReferenceNumber",
                table: "BankStatements");

            migrationBuilder.DropColumn(
                name: "UpdatedByUserId",
                table: "BankStatements");

            migrationBuilder.DropColumn(
                name: "UpdatedOnUtc",
                table: "BankStatements");

            migrationBuilder.DropColumn(
                name: "YearId",
                table: "BankStatements");

            migrationBuilder.DropColumn(
                name: "DocumentReference",
                table: "AccountsEntryDocuments");

            migrationBuilder.DropColumn(
                name: "Remarks",
                table: "AccountsEntryDocuments");

            migrationBuilder.DropColumn(
                name: "AccountCode",
                table: "AccountsChartAccounts");

            migrationBuilder.DropColumn(
                name: "AccountLimit",
                table: "AccountsChartAccounts");

            migrationBuilder.DropColumn(
                name: "AccountReference",
                table: "AccountsChartAccounts");

            migrationBuilder.DropColumn(
                name: "Address",
                table: "AccountsChartAccounts");

            migrationBuilder.DropColumn(
                name: "Attachment",
                table: "AccountsChartAccounts");

            migrationBuilder.DropColumn(
                name: "BalanceAmount",
                table: "AccountsChartAccounts");

            migrationBuilder.DropColumn(
                name: "BankAccountNumber",
                table: "AccountsChartAccounts");

            migrationBuilder.DropColumn(
                name: "BudgetAmount",
                table: "AccountsChartAccounts");

            migrationBuilder.DropColumn(
                name: "CnicNtn",
                table: "AccountsChartAccounts");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "AccountsChartAccounts");

            migrationBuilder.DropColumn(
                name: "CreatedOnUtc",
                table: "AccountsChartAccounts");

            migrationBuilder.DropColumn(
                name: "Credit",
                table: "AccountsChartAccounts");

            migrationBuilder.DropColumn(
                name: "Debit",
                table: "AccountsChartAccounts");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "AccountsChartAccounts");

            migrationBuilder.DropColumn(
                name: "DesignationId",
                table: "AccountsChartAccounts");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "AccountsChartAccounts");

            migrationBuilder.DropColumn(
                name: "FullName",
                table: "AccountsChartAccounts");

            migrationBuilder.DropColumn(
                name: "IsInventory",
                table: "AccountsChartAccounts");

            migrationBuilder.DropColumn(
                name: "IsStaff",
                table: "AccountsChartAccounts");

            migrationBuilder.DropColumn(
                name: "IsStatement",
                table: "AccountsChartAccounts");

            migrationBuilder.DropColumn(
                name: "ParentId",
                table: "AccountsChartAccounts");

            migrationBuilder.DropColumn(
                name: "PersonId",
                table: "AccountsChartAccounts");

            migrationBuilder.DropColumn(
                name: "Phone",
                table: "AccountsChartAccounts");

            migrationBuilder.DropColumn(
                name: "StatusId",
                table: "AccountsChartAccounts");

            migrationBuilder.DropColumn(
                name: "UpdatedByUserId",
                table: "AccountsChartAccounts");

            migrationBuilder.DropColumn(
                name: "UpdatedOnUtc",
                table: "AccountsChartAccounts");

            migrationBuilder.DropColumn(
                name: "UsedAmount",
                table: "AccountsChartAccounts");

            migrationBuilder.DropColumn(
                name: "BalanceAmount",
                table: "AccountsCategories");

            migrationBuilder.DropColumn(
                name: "BudgetAmount",
                table: "AccountsCategories");

            migrationBuilder.DropColumn(
                name: "CategoryTypeId",
                table: "AccountsCategories");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "AccountsCategories");

            migrationBuilder.DropColumn(
                name: "CreatedOnUtc",
                table: "AccountsCategories");

            migrationBuilder.DropColumn(
                name: "IsNotInReport",
                table: "AccountsCategories");

            migrationBuilder.DropColumn(
                name: "Number",
                table: "AccountsCategories");

            migrationBuilder.DropColumn(
                name: "ReferenceNumber",
                table: "AccountsCategories");

            migrationBuilder.DropColumn(
                name: "UpdatedByUserId",
                table: "AccountsCategories");

            migrationBuilder.DropColumn(
                name: "UpdatedOnUtc",
                table: "AccountsCategories");

            migrationBuilder.DropColumn(
                name: "UsedAmount",
                table: "AccountsCategories");
        }
    }
}
