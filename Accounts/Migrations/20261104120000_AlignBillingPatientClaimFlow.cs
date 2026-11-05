using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Reflection;

#nullable disable
namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261104120000_AlignBillingPatientClaimFlow")]
public sealed class AlignBillingPatientClaimFlow : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
IF COL_LENGTH('dbo.tblPerson','OrgId') IS NULL
    ALTER TABLE dbo.tblPerson ADD OrgId int NULL;
""");

        migrationBuilder.Sql("""
UPDATE personRow
SET personRow.TenantId = COALESCE(personRow.TenantId, patient.TenantId),
    personRow.OrgId = COALESCE(personRow.OrgId, patient.OrgId)
FROM dbo.tblPerson personRow
INNER JOIN dbo.tblPatient patient ON patient.PersonId = personRow.PersonId
WHERE personRow.TenantId IS NULL OR personRow.OrgId IS NULL;

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.tblPatient') AND name='IX_tblPatient_Tenant_Org_PatientNo')
    CREATE INDEX IX_tblPatient_Tenant_Org_PatientNo
    ON dbo.tblPatient(TenantId,OrgId,PatientNo) INCLUDE(PatientId,PersonId,OfficeId);

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.tblAppointment') AND name='IX_tblAppointment_Tenant_Org_DOS')
    CREATE INDEX IX_tblAppointment_Tenant_Org_DOS
    ON dbo.tblAppointment(TenantId,OrgId,DOS) INCLUDE(AppointmentId,PatientId,OfficeId,PtTypeId,IsClaimed);

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.tblInsPTClaims') AND name='IX_tblInsPTClaims_Tenant_Org_Submitted')
    CREATE INDEX IX_tblInsPTClaims_Tenant_Org_Submitted
    ON dbo.tblInsPTClaims(TenantId,OrgId,SubmittedDate,StatusId)
    INCLUDE(ClaimId,AppointmentId,PatientId,CarrierId,EobStatusId);

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.tblInsPTClaims') AND name='UX_tblInsPTClaims_Tenant_Org_Appointment')
   AND NOT EXISTS(
       SELECT 1 FROM dbo.tblInsPTClaims
       WHERE TenantId IS NOT NULL AND OrgId IS NOT NULL AND AppointmentId IS NOT NULL
       GROUP BY TenantId,OrgId,AppointmentId HAVING COUNT(*)>1)
    CREATE UNIQUE INDEX UX_tblInsPTClaims_Tenant_Org_Appointment
    ON dbo.tblInsPTClaims(TenantId,OrgId,AppointmentId)
    WHERE TenantId IS NOT NULL AND OrgId IS NOT NULL AND AppointmentId IS NOT NULL;
""");

        ApplyEmbeddedProcedure(migrationBuilder, "Accounts.Sql.StoredProcedures.usp_Billing_PatientMaster_List.sql");
        ApplyEmbeddedProcedure(migrationBuilder, "Accounts.Sql.StoredProcedures.usp_Billing_Claims_List.sql");
    }

    private static void ApplyEmbeddedProcedure(MigrationBuilder migrationBuilder, string resourceName)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Missing embedded SQL resource: {resourceName}");
        using var reader = new StreamReader(stream);
        var sql = reader.ReadToEnd();
        foreach (var batch in System.Text.RegularExpressions.Regex.Split(
                     sql,
                     @"^\s*GO\s*$",
                     System.Text.RegularExpressions.RegexOptions.Multiline |
                     System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(batch)) migrationBuilder.Sql(batch);
        }
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Billing data and indexes are intentionally retained on rollback.
    }
}
