using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class AddPayAndAllowancesModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EobiEligibilities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EobiNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    IsEligible = table.Column<bool>(type: "bit", nullable: false),
                    Remarks = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EobiEligibilities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EobiEligibilities_Persons_PersonId",
                        column: x => x.PersonId,
                        principalTable: "Persons",
                        principalColumn: "PersonId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EobiEligibilities_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EobiSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    EmployeeRatePercentage = table.Column<decimal>(type: "decimal(9,4)", nullable: false),
                    EmployerRatePercentage = table.Column<decimal>(type: "decimal(9,4)", nullable: false),
                    MinimumWage = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    MaximumContributionBase = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EobiSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EobiSettings_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PayrollBenefitDefinitions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IsEobiContributory = table.Column<bool>(type: "bit", nullable: false),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    CalculationType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Percentage = table.Column<decimal>(type: "decimal(9,4)", nullable: false),
                    IsTaxable = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollBenefitDefinitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollBenefitDefinitions_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PayrollBonusDefinitions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Frequency = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    CalculationType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Percentage = table.Column<decimal>(type: "decimal(9,4)", nullable: false),
                    IsTaxable = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollBonusDefinitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollBonusDefinitions_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PayrollRuns",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Month = table.Column<int>(type: "int", nullable: false),
                    RunNumber = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    PayDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollRuns_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PayrollTaxSlabs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    TaxYear = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FromAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ToAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    FixedTaxAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    RatePercentage = table.Column<decimal>(type: "decimal(9,4)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollTaxSlabs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollTaxSlabs_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EobiEligibilities_PersonId",
                table: "EobiEligibilities",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_EobiEligibilities_TenantId_PersonId",
                table: "EobiEligibilities",
                columns: new[] { "TenantId", "PersonId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EobiSettings_TenantId_EffectiveFrom",
                table: "EobiSettings",
                columns: new[] { "TenantId", "EffectiveFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollBenefitDefinitions_TenantId_Code",
                table: "PayrollBenefitDefinitions",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollBenefitDefinitions_TenantId_Name",
                table: "PayrollBenefitDefinitions",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollBonusDefinitions_TenantId_Code",
                table: "PayrollBonusDefinitions",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollBonusDefinitions_TenantId_Name",
                table: "PayrollBonusDefinitions",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRuns_TenantId_RunNumber",
                table: "PayrollRuns",
                columns: new[] { "TenantId", "RunNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRuns_TenantId_Year_Month",
                table: "PayrollRuns",
                columns: new[] { "TenantId", "Year", "Month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollTaxSlabs_TenantId_TaxYear_FromAmount",
                table: "PayrollTaxSlabs",
                columns: new[] { "TenantId", "TaxYear", "FromAmount" },
                unique: true);

            if (ActiveProvider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
                migrationBuilder.Sql(MenuSeedSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EobiEligibilities");

            migrationBuilder.DropTable(
                name: "EobiSettings");

            migrationBuilder.DropTable(
                name: "PayrollBenefitDefinitions");

            migrationBuilder.DropTable(
                name: "PayrollBonusDefinitions");

            migrationBuilder.DropTable(
                name: "PayrollRuns");

            migrationBuilder.DropTable(
                name: "PayrollTaxSlabs");
        }

        private const string MenuSeedSql = """
            DECLARE @ParentId int = (SELECT TOP (1) Id FROM dbo.Menus WHERE ParentId IS NULL AND Title IN (N'Pay & Allowances',N'Pay And Allowances') ORDER BY Id);
            IF @ParentId IS NULL BEGIN INSERT dbo.Menus(Title,Icon,Route,ParentId,SortOrder,IsActive) VALUES(N'Pay & Allowances',N'BadgeDollarSign',NULL,NULL,90,1); SET @ParentId=SCOPE_IDENTITY(); END
            ELSE UPDATE dbo.Menus SET Title=N'Pay & Allowances',Icon=N'BadgeDollarSign',Route=NULL,SortOrder=90,IsActive=1 WHERE Id=@ParentId;

            DECLARE @Seed TABLE(Id int,Title nvarchar(100),Icon nvarchar(100),Route nvarchar(300),SortOrder int);
            INSERT @Seed(Title,Icon,Route,SortOrder) VALUES
              (N'Pay Scale',N'Landmark',N'/pay-allowances/pay-scale',1),
              (N'Benefits',N'HeartHandshake',N'/pay-allowances/benefits',2),
              (N'Bonus',N'Gift',N'/pay-allowances/bonus',3),
              (N'Pay Roll',N'WalletCards',N'/pay-allowances/payroll',4),
              (N'EOBI',N'ShieldCheck',N'/pay-allowances/eobi',5),
              (N'Tax',N'ReceiptText',N'/pay-allowances/tax',6),
              (N'EOBI Elig List',N'UsersRound',N'/pay-allowances/eobi-eligibility',7);

            DECLARE @Title nvarchar(100), @Icon nvarchar(100), @Route nvarchar(300), @Sort int, @Id int;
            DECLARE menu_cursor CURSOR LOCAL FAST_FORWARD FOR SELECT Title,Icon,Route,SortOrder FROM @Seed;
            OPEN menu_cursor; FETCH NEXT FROM menu_cursor INTO @Title,@Icon,@Route,@Sort;
            WHILE @@FETCH_STATUS=0 BEGIN
              SET @Id=(SELECT TOP(1) Id FROM dbo.Menus WHERE Route=@Route ORDER BY Id);
              IF @Id IS NULL BEGIN INSERT dbo.Menus(Title,Icon,Route,ParentId,SortOrder,IsActive) VALUES(@Title,@Icon,@Route,@ParentId,@Sort,1); SET @Id=SCOPE_IDENTITY(); END
              ELSE UPDATE dbo.Menus SET Title=@Title,Icon=@Icon,ParentId=@ParentId,SortOrder=@Sort,IsActive=1 WHERE Id=@Id;
              UPDATE @Seed SET Id=@Id WHERE Route=@Route;
              FETCH NEXT FROM menu_cursor INTO @Title,@Icon,@Route,@Sort;
            END
            CLOSE menu_cursor; DEALLOCATE menu_cursor;

            INSERT dbo.Features(FeatureKey,FeatureName,Module,Description,CreatedDate)
            SELECT CONCAT(N'MENU_',m.Id,s.Suffix),CONCAT(m.Title,s.DisplayName),N'Pay & Allowances',CONCAT(s.ActionName,N' ',m.Title),SYSUTCDATETIME()
            FROM (SELECT @ParentId Id,N'Pay & Allowances' Title UNION ALL SELECT Id,Title FROM @Seed) m
            CROSS JOIN (VALUES(N'',N'',N'Open'),(N'_VIEW',N' - View',N'View'),(N'_ADD',N' - Add',N'Add'),(N'_EDIT',N' - Edit',N'Edit'),(N'_DELETE',N' - Delete',N'Delete')) s(Suffix,DisplayName,ActionName)
            WHERE NOT EXISTS(SELECT 1 FROM dbo.Features f WHERE f.FeatureKey=CONCAT(N'MENU_',m.Id,s.Suffix));

            INSERT dbo.MenuPermissions(MenuId,PermissionId)
            SELECT m.Id,f.PermissionId FROM (SELECT @ParentId Id UNION ALL SELECT Id FROM @Seed) m
            JOIN dbo.Features f ON f.FeatureKey IN(CONCAT(N'MENU_',m.Id),CONCAT(N'MENU_',m.Id,N'_VIEW'),CONCAT(N'MENU_',m.Id,N'_ADD'),CONCAT(N'MENU_',m.Id,N'_EDIT'),CONCAT(N'MENU_',m.Id,N'_DELETE'))
            WHERE NOT EXISTS(SELECT 1 FROM dbo.MenuPermissions mp WHERE mp.MenuId=m.Id AND mp.PermissionId=f.PermissionId);
            """;
    }
}
