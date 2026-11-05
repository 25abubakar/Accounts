using System.IO;
using System.Reflection;
using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261104190000_AddClaimArAndFlagsParity")]
public sealed class AddClaimArAndFlagsParity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
IF COL_LENGTH('dbo.tblInsPTClaims', 'IsAr') IS NULL
    ALTER TABLE dbo.tblInsPTClaims ADD IsAr bit NULL;

IF COL_LENGTH('dbo.tblInsPTClaims', 'IsNotOriginalRate') IS NULL
    ALTER TABLE dbo.tblInsPTClaims ADD IsNotOriginalRate bit NULL;

IF COL_LENGTH('dbo.tblInsPTClaims', 'IsRep') IS NULL
    ALTER TABLE dbo.tblInsPTClaims ADD IsRep bit NULL;

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.tblInsPTClaims') AND name='IX_tblInsPTClaims_Tenant_Org_IsAr')
    CREATE INDEX IX_tblInsPTClaims_Tenant_Org_IsAr ON dbo.tblInsPTClaims(TenantId, OrgId, IsAr)
    INCLUDE(SubmittedDate, StatusId, AppointmentId);

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.tblClaimEobDocuments') AND name='IX_tblClaimEobDocuments_Tenant_Claim')
    CREATE INDEX IX_tblClaimEobDocuments_Tenant_Claim 
    ON dbo.tblClaimEobDocuments(TenantId, ClaimId, CreatedDate DESC, Id DESC) 
    INCLUDE(Attachment);

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.tblAppointment') AND name='IX_tblAppointment_Tenant_Org_ApptId_Cover')
    CREATE NONCLUSTERED INDEX IX_tblAppointment_Tenant_Org_ApptId_Cover
    ON dbo.tblAppointment(TenantId, OrgId, AppointmentId)
    INCLUDE (DOS, OfficeId, PtTypeId, InsuranceName);
""");

        ApplyEmbeddedProcedure(migrationBuilder, "Accounts.Sql.StoredProcedures.usp_Billing_Claims_List.sql");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
IF EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.tblInsPTClaims') AND name='IX_tblInsPTClaims_Tenant_Org_IsAr')
    DROP INDEX IX_tblInsPTClaims_Tenant_Org_IsAr ON dbo.tblInsPTClaims;
""");
    }

    private static void ApplyEmbeddedProcedure(MigrationBuilder migrationBuilder, string resourceName)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Missing embedded SQL resource: {resourceName}");
        using var reader = new StreamReader(stream);
        var sql = reader.ReadToEnd();
        migrationBuilder.Sql(sql);
    }
}

