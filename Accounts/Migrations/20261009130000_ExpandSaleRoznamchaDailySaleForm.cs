using Accounts.Data;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

public partial class ExpandSaleRoznamchaDailySaleForm : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        CreateLookupTables(migrationBuilder);

        migrationBuilder.AddColumn<string>("MarketplaceCompanyId", "SaleRoznamchaDailySales", "nvarchar(100)", maxLength: 100, nullable: true);
        migrationBuilder.AddColumn<string>("OrderId", "SaleRoznamchaDailySales", "nvarchar(100)", maxLength: 100, nullable: true);
        migrationBuilder.AddColumn<decimal>("GrossTotal", "SaleRoznamchaDailySales", "decimal(18,2)", nullable: false, defaultValue: 0m);
        migrationBuilder.AddColumn<decimal>("TotalRevenue", "SaleRoznamchaDailySales", "decimal(18,2)", nullable: false, defaultValue: 0m);
        migrationBuilder.AddColumn<decimal>("CostAmount", "SaleRoznamchaDailySales", "decimal(18,2)", nullable: false, defaultValue: 0m);
        migrationBuilder.AddColumn<decimal>("PayCharges", "SaleRoznamchaDailySales", "decimal(18,2)", nullable: false, defaultValue: 0m);
        migrationBuilder.AddColumn<decimal>("DeliveryFee", "SaleRoznamchaDailySales", "decimal(18,2)", nullable: false, defaultValue: 0m);
        migrationBuilder.AddColumn<decimal>("OtherAmount", "SaleRoznamchaDailySales", "decimal(18,2)", nullable: false, defaultValue: 0m);
        migrationBuilder.AddColumn<decimal>("PromotionAmount", "SaleRoznamchaDailySales", "decimal(18,2)", nullable: false, defaultValue: 0m);
        migrationBuilder.AddColumn<decimal>("RtoRefundAmount", "SaleRoznamchaDailySales", "decimal(18,2)", nullable: false, defaultValue: 0m);
        migrationBuilder.AddColumn<int>("TransactionTypeId", "SaleRoznamchaDailySales", "int", nullable: true);
        migrationBuilder.AddColumn<int>("PaidStatusId", "SaleRoznamchaDailySales", "int", nullable: true);
        migrationBuilder.AddColumn<int>("ToAccountId", "SaleRoznamchaDailySales", "int", nullable: true);
        migrationBuilder.AddColumn<string>("InterProReference", "SaleRoznamchaDailySales", "nvarchar(100)", maxLength: 100, nullable: true);
        migrationBuilder.AddColumn<string>("OrderReference", "SaleRoznamchaDailySales", "nvarchar(100)", maxLength: 100, nullable: true);
        migrationBuilder.AddColumn<DateOnly>("SaleDate", "SaleRoznamchaDailySales", "date", nullable: true);
        migrationBuilder.AddColumn<string>("Description", "SaleRoznamchaDailySales", "nvarchar(2000)", maxLength: 2000, nullable: true);

        CreateDetailTables(migrationBuilder);
        SeedLookups(migrationBuilder);
        migrationBuilder.Sql(SaleRoznamchaSql.DailySaleListProcedure);
        migrationBuilder.Sql(SaleRoznamchaSql.DailySaleCreateProcedure);
    }

    private static void CreateLookupTables(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "SaleRoznamchaPaidStatuses",
            columns: table => new
            {
                Id = table.Column<int>("int", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                TenantId = table.Column<int>("int", nullable: false),
                Code = table.Column<string>("nvarchar(30)", maxLength: 30, nullable: false),
                Name = table.Column<string>("nvarchar(100)", maxLength: 100, nullable: false),
                IsActive = table.Column<bool>("bit", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SaleRoznamchaPaidStatuses", x => x.Id);
                table.UniqueConstraint("AK_SaleRoznamchaPaidStatuses_TenantId_Id", x => new { x.TenantId, x.Id });
                table.ForeignKey("FK_SaleRoznamchaPaidStatuses_Tenants_TenantId", x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex("IX_SaleRoznamchaPaidStatuses_TenantId_Code", "SaleRoznamchaPaidStatuses", new[] { "TenantId", "Code" }, unique: true);

        migrationBuilder.CreateTable(
            name: "SaleRoznamchaChargeLabels",
            columns: table => new
            {
                Id = table.Column<int>("int", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                TenantId = table.Column<int>("int", nullable: false),
                Code = table.Column<string>("nvarchar(30)", maxLength: 30, nullable: false),
                Name = table.Column<string>("nvarchar(100)", maxLength: 100, nullable: false),
                SortOrder = table.Column<int>("int", nullable: false),
                IsActive = table.Column<bool>("bit", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SaleRoznamchaChargeLabels", x => x.Id);
                table.UniqueConstraint("AK_SaleRoznamchaChargeLabels_TenantId_Id", x => new { x.TenantId, x.Id });
                table.ForeignKey("FK_SaleRoznamchaChargeLabels_Tenants_TenantId", x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex("IX_SaleRoznamchaChargeLabels_TenantId_Code", "SaleRoznamchaChargeLabels", new[] { "TenantId", "Code" }, unique: true);

        migrationBuilder.CreateTable(
            name: "SaleRoznamchaTransactionDateLabels",
            columns: table => new
            {
                Id = table.Column<int>("int", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                TenantId = table.Column<int>("int", nullable: false),
                Code = table.Column<string>("nvarchar(30)", maxLength: 30, nullable: false),
                Name = table.Column<string>("nvarchar(100)", maxLength: 100, nullable: false),
                SortOrder = table.Column<int>("int", nullable: false),
                IsActive = table.Column<bool>("bit", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SaleRoznamchaTransactionDateLabels", x => x.Id);
                table.UniqueConstraint("AK_SaleRoznamchaTransactionDateLabels_TenantId_Id", x => new { x.TenantId, x.Id });
                table.ForeignKey("FK_SaleRoznamchaTransactionDateLabels_Tenants_TenantId", x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex("IX_SaleRoznamchaTransactionDateLabels_TenantId_Code", "SaleRoznamchaTransactionDateLabels", new[] { "TenantId", "Code" }, unique: true);
    }

    private static void CreateDetailTables(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "SaleRoznamchaDailySaleCharges",
            columns: table => new
            {
                Id = table.Column<long>("bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                TenantId = table.Column<int>("int", nullable: false),
                DailySaleId = table.Column<long>("bigint", nullable: false),
                ChargeLabelId = table.Column<int>("int", nullable: false),
                Amount = table.Column<decimal>("decimal(18,2)", nullable: false),
                CreatedOnUtc = table.Column<DateTime>("datetime2", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SaleRoznamchaDailySaleCharges", x => x.Id);
                table.ForeignKey("FK_SaleRoznamchaDailySaleCharges_Tenants_TenantId", x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_SaleRoznamchaDailySaleCharges_SaleRoznamchaDailySales_TenantId_DailySaleId", x => new { x.TenantId, x.DailySaleId }, "SaleRoznamchaDailySales", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_SaleRoznamchaDailySaleCharges_SaleRoznamchaChargeLabels_TenantId_ChargeLabelId", x => new { x.TenantId, x.ChargeLabelId }, "SaleRoznamchaChargeLabels", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex("IX_SaleRoznamchaDailySaleCharges_TenantId_DailySaleId_ChargeLabelId", "SaleRoznamchaDailySaleCharges", new[] { "TenantId", "DailySaleId", "ChargeLabelId" });
        migrationBuilder.CreateIndex("IX_SaleRoznamchaDailySaleCharges_TenantId_ChargeLabelId", "SaleRoznamchaDailySaleCharges", new[] { "TenantId", "ChargeLabelId" });

        migrationBuilder.CreateTable(
            name: "SaleRoznamchaDailySaleTransactionDates",
            columns: table => new
            {
                Id = table.Column<long>("bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                TenantId = table.Column<int>("int", nullable: false),
                DailySaleId = table.Column<long>("bigint", nullable: false),
                DateLabelId = table.Column<int>("int", nullable: false),
                Value = table.Column<DateOnly>("date", nullable: false),
                CreatedOnUtc = table.Column<DateTime>("datetime2", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SaleRoznamchaDailySaleTransactionDates", x => x.Id);
                table.ForeignKey("FK_SaleRoznamchaDailySaleTransactionDates_Tenants_TenantId", x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_SaleRoznamchaDailySaleTransactionDates_SaleRoznamchaDailySales_TenantId_DailySaleId", x => new { x.TenantId, x.DailySaleId }, "SaleRoznamchaDailySales", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_SaleRoznamchaDailySaleTransactionDates_SaleRoznamchaTransactionDateLabels_TenantId_DateLabelId", x => new { x.TenantId, x.DateLabelId }, "SaleRoznamchaTransactionDateLabels", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex("IX_SaleRoznamchaDailySaleTransactionDates_TenantId_DailySaleId_DateLabelId", "SaleRoznamchaDailySaleTransactionDates", new[] { "TenantId", "DailySaleId", "DateLabelId" });
        migrationBuilder.CreateIndex("IX_SaleRoznamchaDailySaleTransactionDates_TenantId_DateLabelId", "SaleRoznamchaDailySaleTransactionDates", new[] { "TenantId", "DateLabelId" });
    }

    private static void SeedLookups(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(
        """
        INSERT INTO dbo.SaleRoznamchaPaidStatuses (TenantId, Code, Name, IsActive)
        SELECT tenant.Id, seed.Code, seed.Name, 1
        FROM dbo.Tenants tenant
        CROSS JOIN (VALUES (N'UNPAID', N'Unpaid'), (N'PAID', N'Paid')) seed(Code, Name)
        WHERE NOT EXISTS (SELECT 1 FROM dbo.SaleRoznamchaPaidStatuses currentRow WHERE currentRow.TenantId = tenant.Id AND currentRow.Code = seed.Code);

        INSERT INTO dbo.SaleRoznamchaChargeLabels (TenantId, Code, Name, SortOrder, IsActive)
        SELECT tenant.Id, seed.Code, seed.Name, seed.SortOrder, 1
        FROM dbo.Tenants tenant
        CROSS JOIN (VALUES
            (N'CHARGE_1', N'Charge 1', 1), (N'CHARGE_2', N'Charge 2', 2),
            (N'CHARGE_3', N'Charge 3', 3), (N'CHARGE_4', N'Charge 4', 4)
        ) seed(Code, Name, SortOrder)
        WHERE NOT EXISTS (SELECT 1 FROM dbo.SaleRoznamchaChargeLabels currentRow WHERE currentRow.TenantId = tenant.Id AND currentRow.Code = seed.Code);

        INSERT INTO dbo.SaleRoznamchaTransactionDateLabels (TenantId, Code, Name, SortOrder, IsActive)
        SELECT tenant.Id, seed.Code, seed.Name, seed.SortOrder, 1
        FROM dbo.Tenants tenant
        CROSS JOIN (VALUES
            (N'DATE_1', N'Date 1', 1), (N'DATE_2', N'Date 2', 2), (N'DATE_3', N'Date 3', 3),
            (N'DATE_4', N'Date 4', 4), (N'DATE_5', N'Date 5', 5)
        ) seed(Code, Name, SortOrder)
        WHERE NOT EXISTS (SELECT 1 FROM dbo.SaleRoznamchaTransactionDateLabels currentRow WHERE currentRow.TenantId = tenant.Id AND currentRow.Code = seed.Code);
        """);

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.usp_SaleRoznamcha_DailySaleCreate;");
        migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.usp_SaleRoznamcha_DailySaleList;");
        migrationBuilder.DropTable("SaleRoznamchaDailySaleCharges");
        migrationBuilder.DropTable("SaleRoznamchaDailySaleTransactionDates");
        migrationBuilder.DropTable("SaleRoznamchaChargeLabels");
        migrationBuilder.DropTable("SaleRoznamchaPaidStatuses");
        migrationBuilder.DropTable("SaleRoznamchaTransactionDateLabels");
        foreach (var column in new[]
        {
            "MarketplaceCompanyId", "OrderId", "GrossTotal", "TotalRevenue", "CostAmount", "PayCharges",
            "DeliveryFee", "OtherAmount", "PromotionAmount", "RtoRefundAmount", "TransactionTypeId",
            "PaidStatusId", "ToAccountId", "InterProReference", "OrderReference", "SaleDate", "Description",
        }) migrationBuilder.DropColumn(column, "SaleRoznamchaDailySales");
    }
}
