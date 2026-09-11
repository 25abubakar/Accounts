using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260911150000_AddBenefitRuleOrgAndContractScopes")]
public sealed class AddBenefitRuleOrgAndContractScopes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'dbo.PayrollBenefitRuleOrganizations', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.PayrollBenefitRuleOrganizations
                (
                    Id              INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PayrollBenefitRuleOrganizations PRIMARY KEY,
                    TenantId        INT NOT NULL,
                    BenefitRuleId   INT NOT NULL,
                    OrganizationId  INT NOT NULL,
                    ScopeLabel      NVARCHAR(30) NOT NULL CONSTRAINT DF_PayrollBenefitRuleOrgs_Scope DEFAULT(N'Department'),
                    CONSTRAINT FK_PayrollBenefitRuleOrgs_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id),
                    CONSTRAINT FK_PayrollBenefitRuleOrgs_Rules FOREIGN KEY (BenefitRuleId) REFERENCES dbo.PayrollBenefitRules(Id)
                );
                CREATE UNIQUE INDEX IX_PayrollBenefitRuleOrgs_Tenant_Rule_Org
                    ON dbo.PayrollBenefitRuleOrganizations (TenantId, BenefitRuleId, OrganizationId);
                CREATE INDEX IX_PayrollBenefitRuleOrgs_Rule
                    ON dbo.PayrollBenefitRuleOrganizations (BenefitRuleId);
            END

            IF OBJECT_ID(N'dbo.PayrollBenefitRuleContracts', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.PayrollBenefitRuleContracts
                (
                    Id              INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PayrollBenefitRuleContracts PRIMARY KEY,
                    TenantId        INT NOT NULL,
                    BenefitRuleId   INT NOT NULL,
                    ContractName    NVARCHAR(50) NOT NULL,
                    CONSTRAINT FK_PayrollBenefitRuleContracts_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id),
                    CONSTRAINT FK_PayrollBenefitRuleContracts_Rules FOREIGN KEY (BenefitRuleId) REFERENCES dbo.PayrollBenefitRules(Id)
                );
                CREATE UNIQUE INDEX IX_PayrollBenefitRuleContracts_Tenant_Rule_Name
                    ON dbo.PayrollBenefitRuleContracts (TenantId, BenefitRuleId, ContractName);
                CREATE INDEX IX_PayrollBenefitRuleContracts_Rule
                    ON dbo.PayrollBenefitRuleContracts (BenefitRuleId);
            END

            -- Backfill org scopes from legacy OrganizationId
            INSERT INTO dbo.PayrollBenefitRuleOrganizations (TenantId, BenefitRuleId, OrganizationId, ScopeLabel)
            SELECT r.TenantId, r.Id, r.OrganizationId,
                   CASE
                       WHEN EXISTS (
                           SELECT 1 FROM dbo.OrganizationTree o
                           WHERE o.Id = r.OrganizationId
                             AND LOWER(REPLACE(REPLACE(ISNULL(o.Label, N''), N'-', N' '), N'_', N' ')) = N'company'
                       ) THEN N'Company'
                       WHEN EXISTS (
                           SELECT 1 FROM dbo.OrganizationTree o
                           WHERE o.Id = r.OrganizationId
                             AND (
                                 LOWER(REPLACE(REPLACE(ISNULL(o.Label, N''), N'-', N' '), N'_', N' ')) IN (N'branch', N'sub branch', N'subbranch', N'office')
                             )
                       ) THEN N'Branch'
                       ELSE N'Department'
                   END
            FROM dbo.PayrollBenefitRules r
            WHERE r.OrganizationId IS NOT NULL
              AND NOT EXISTS (
                  SELECT 1 FROM dbo.PayrollBenefitRuleOrganizations x
                  WHERE x.TenantId = r.TenantId AND x.BenefitRuleId = r.Id AND x.OrganizationId = r.OrganizationId
              );

            -- Backfill contracts from legacy Contract column (CSV supported)
            ;WITH SplitContracts AS
            (
                SELECT r.TenantId, r.Id AS BenefitRuleId, LTRIM(RTRIM(value)) AS ContractName
                FROM dbo.PayrollBenefitRules r
                CROSS APPLY STRING_SPLIT(REPLACE(ISNULL(r.Contract, N''), N';', N','), N',')
                WHERE NULLIF(LTRIM(RTRIM(r.Contract)), N'') IS NOT NULL
            )
            INSERT INTO dbo.PayrollBenefitRuleContracts (TenantId, BenefitRuleId, ContractName)
            SELECT DISTINCT s.TenantId, s.BenefitRuleId, s.ContractName
            FROM SplitContracts s
            WHERE s.ContractName <> N''
              AND NOT EXISTS (
                  SELECT 1 FROM dbo.PayrollBenefitRuleContracts x
                  WHERE x.TenantId = s.TenantId AND x.BenefitRuleId = s.BenefitRuleId AND x.ContractName = s.ContractName
              );

            IF NOT EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId = N'20260911150000_AddBenefitRuleOrgAndContractScopes')
                INSERT INTO dbo.__EFMigrationsHistory (MigrationId, ProductVersion)
                VALUES (N'20260911150000_AddBenefitRuleOrgAndContractScopes', N'9.0.0');
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'dbo.PayrollBenefitRuleContracts', N'U') IS NOT NULL
                DROP TABLE dbo.PayrollBenefitRuleContracts;
            IF OBJECT_ID(N'dbo.PayrollBenefitRuleOrganizations', N'U') IS NOT NULL
                DROP TABLE dbo.PayrollBenefitRuleOrganizations;
            """);
    }
}
