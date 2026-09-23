using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

/// <summary>
/// Account Type → Date tab master (legacy tblAccountDates / DateLabel).
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260923140000_AddAccountsDateLabels")]
public sealed class AddAccountsDateLabels : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'dbo.AccountsDateLabels', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.AccountsDateLabels
                (
                    Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AccountsDateLabels PRIMARY KEY,
                    TenantId INT NULL,
                    Code NVARCHAR(40) NOT NULL,
                    Name NVARCHAR(120) NOT NULL,
                    IsActive BIT NOT NULL CONSTRAINT DF_AccountsDateLabels_IsActive DEFAULT (1),
                    CONSTRAINT FK_AccountsDateLabels_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id)
                );
                CREATE UNIQUE INDEX IX_AccountsDateLabels_Tenant_Code
                    ON dbo.AccountsDateLabels (TenantId, Code);
            END
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'dbo.AccountsDateLabels', N'U') IS NOT NULL
                DROP TABLE dbo.AccountsDateLabels;
            """);
    }
}
