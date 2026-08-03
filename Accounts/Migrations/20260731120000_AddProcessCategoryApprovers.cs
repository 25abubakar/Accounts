using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260731120000_AddProcessCategoryApprovers")]
public sealed class AddProcessCategoryApprovers : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'dbo.ProcessCategoryApprovers', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.ProcessCategoryApprovers
                (
                    Id          int              IDENTITY(1,1) NOT NULL CONSTRAINT PK_ProcessCategoryApprovers PRIMARY KEY,
                    TenantId    int              NOT NULL,
                    CategoryId  int              NOT NULL,
                    StaffId     uniqueidentifier NOT NULL,
                    CreatedDateUtc datetime2     NOT NULL CONSTRAINT DF_ProcessCategoryApprovers_CreatedDateUtc DEFAULT SYSUTCDATETIME(),
                    CreatedByUserId nvarchar(450) NOT NULL,
                    CONSTRAINT UQ_ProcessCategoryApprovers_TenantCategoryStaff UNIQUE (TenantId, CategoryId, StaffId),
                    CONSTRAINT FK_ProcessCategoryApprovers_Tenant    FOREIGN KEY (TenantId)   REFERENCES dbo.Tenants(Id),
                    CONSTRAINT FK_ProcessCategoryApprovers_Category  FOREIGN KEY (CategoryId) REFERENCES dbo.ProcessWorkflowCategories(Id),
                    CONSTRAINT FK_ProcessCategoryApprovers_Staff     FOREIGN KEY (StaffId)    REFERENCES dbo.StaffVacancy(StaffId)
                );
                CREATE INDEX IX_ProcessCategoryApprovers_Tenant ON dbo.ProcessCategoryApprovers (TenantId, CategoryId);
            END;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TABLE IF EXISTS dbo.ProcessCategoryApprovers;");
    }
}
