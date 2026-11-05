using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261104133000_FixTenant3BillingOrganizationScope")]
public sealed class FixTenant3BillingOrganizationScope : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
IF NOT EXISTS(SELECT 1 FROM dbo.Tenants WHERE Id=3 AND OrganizationTreeId=204)
    THROW 51000, 'Tenant 3 is not mapped to organization 204; billing scope correction was not applied.', 1;

UPDATE dbo.tblPerson SET OrgId=204 WHERE TenantId=3 AND (OrgId IS NULL OR OrgId=3);
UPDATE dbo.tblPatient SET OrgId=204 WHERE TenantId=3 AND (OrgId IS NULL OR OrgId=3);
UPDATE dbo.tblAppointment SET OrgId=204 WHERE TenantId=3 AND (OrgId IS NULL OR OrgId=3);
UPDATE dbo.tblInsPTClaims SET OrgId=204 WHERE TenantId=3 AND (OrgId IS NULL OR OrgId=3);
UPDATE dbo.tblApptCodes SET OrgId=204 WHERE TenantId=3 AND (OrgId IS NULL OR OrgId=3);
UPDATE dbo.tblClaimCodeAmountDetails SET OrgId=204 WHERE TenantId=3 AND (OrgId IS NULL OR OrgId=3);
UPDATE dbo.tblClaimGrpCodeDetails SET OrgId=204 WHERE TenantId=3 AND (OrgId IS NULL OR OrgId=3);
UPDATE dbo.tblCarrier SET OrgId=204 WHERE TenantId=3 AND (OrgId IS NULL OR OrgId=3);
""");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Correct tenant-3 ownership is intentionally retained.
    }
}
