using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

public partial class AddTenantPlatformTypes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "PlatformTypeCategories",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false),
                Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                Icon = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                DisplayOrder = table.Column<int>(type: "int", nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_PlatformTypeCategories", x => x.Id));

        migrationBuilder.CreateTable(
            name: "PlatformTypeValues",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                TenantId = table.Column<int>(type: "int", nullable: false),
                CategoryId = table.Column<int>(type: "int", nullable: false),
                Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                Code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                DisplayOrder = table.Column<int>(type: "int", nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false),
                CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                ModifiedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                ModifiedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PlatformTypeValues", x => x.Id);
                table.ForeignKey("FK_PlatformTypeValues_PlatformTypeCategories_CategoryId", x => x.CategoryId, "PlatformTypeCategories", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_PlatformTypeValues_Tenants_TenantId", x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex("IX_PlatformTypeCategories_Code", "PlatformTypeCategories", "Code", unique: true);
        migrationBuilder.CreateIndex("IX_PlatformTypeValues_CategoryId", "PlatformTypeValues", "CategoryId");
        migrationBuilder.CreateIndex("IX_PlatformTypeValues_TenantId_CategoryId_Code", "PlatformTypeValues", new[] { "TenantId", "CategoryId", "Code" }, unique: true);
        migrationBuilder.CreateIndex("IX_PlatformTypeValues_TenantId_CategoryId_DisplayOrder", "PlatformTypeValues", new[] { "TenantId", "CategoryId", "DisplayOrder" });

        migrationBuilder.Sql("""
            INSERT INTO dbo.PlatformTypeCategories (Id,Code,Name,Icon,DisplayOrder,IsActive) VALUES
            (1,N'CONTRACT',N'Contract',N'FileText',1,1),
            (2,N'FREQUENCY',N'Frequency',N'Repeat2',2,1),
            (3,N'RATE',N'Rate',N'BadgeDollarSign',3,1),
            (4,N'ALLOWANCE_TYPE',N'Allowance Type',N'Gift',4,1),
            (5,N'TADA_TYPE',N'TADA Type',N'Plane',5,1);

            INSERT INTO dbo.PlatformTypeValues (TenantId,CategoryId,Name,Code,DisplayOrder,IsActive,CreatedOnUtc)
            SELECT tenant.Id, seed.CategoryId, seed.Name, seed.Code, seed.DisplayOrder, 1, SYSUTCDATETIME()
            FROM dbo.Tenants tenant
            CROSS JOIN (VALUES
              (1,N'All',N'ALL',1),(1,N'Regular',N'REGULAR',2),(1,N'Temp',N'TEMP',3),(1,N'Special',N'SPECIAL',4),
              (1,N'Registration',N'REGISTRATION',5),(1,N'Probation',N'PROBATION',6),(1,N'Contract',N'CONTRACT',7),
              (1,N'Part Time',N'PART_TIME',8),(1,N'Casual',N'CASUAL',9),(1,N'Daily',N'DAILY',10),
              (1,N'Visiting',N'VISITING',11),(1,N'Performance',N'PERFORMANCE',12),(1,N'Retired',N'RETIRED',13),
              (1,N'Internship',N'INTERNSHIP',14),(1,N'Hourly',N'HOURLY',15),
              (2,N'PY',N'PY',1),(2,N'PM',N'PM',2),(2,N'PD',N'PD',3),(2,N'One Time',N'ONE_TIME',4),
              (2,N'On Occurrence',N'ON_OCCURRENCE',5),(2,N'On Joining',N'ON_JOINING',6),(2,N'Weekly',N'WEEKLY',8),
              (2,N'Bi-Monthly',N'BI_MONTHLY',10),(2,N'Quarterly',N'QUARTERLY',11),(2,N'Bi-Annually',N'BI_ANNUALLY',13),
              (2,N'On Retirement',N'ON_RETIREMENT',14),(2,N'On Demand',N'ON_DEMAND',15),(2,N'On Travel',N'ON_TRAVEL',16),
              (3,N'Fixed',N'FIXED',1),(3,N'Percentage',N'PERCENTAGE',2),(3,N'Sliding',N'SLIDING',3),
              (3,N'Actual',N'ACTUAL',4),(3,N'Per Mile',N'PER_MILE',5),(3,N'PD',N'PD',6),(3,N'PH',N'PH',7),
              (4,N'Tpt',N'TPT',1),(4,N'Tel',N'TEL',2),(4,N'Med',N'MED',3),(4,N'Night',N'NIGHT',5),
              (4,N'Appt',N'APPT',6),(4,N'Commission',N'COMMISSION',8),(4,N'Proficiency',N'PROFICIENCY',9),
              (5,N'By Road',N'BY_ROAD',1),(5,N'By Rail',N'BY_RAIL',2),(5,N'By Air',N'BY_AIR',3),(5,N'Own Tpt (Rs PM)',N'OWN_TPT_RS_PM',4)
            ) seed(CategoryId,Name,Code,DisplayOrder);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "PlatformTypeValues");
        migrationBuilder.DropTable(name: "PlatformTypeCategories");
    }
}
