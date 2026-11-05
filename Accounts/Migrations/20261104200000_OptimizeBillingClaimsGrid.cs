using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Reflection;

#nullable disable
namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261104200000_OptimizeBillingClaimsGrid")]
public sealed class OptimizeBillingClaimsGrid : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.tblInsPTClaims') AND name='IX_tblInsPTClaims_BillingClaimsGrid')
 CREATE INDEX IX_tblInsPTClaims_BillingClaimsGrid
 ON dbo.tblInsPTClaims(TenantId,OrgId,StatusId,SubmittedDate,AppointmentId)
 INCLUDE(ClaimId,PatientId,CarrierId,EobStatusId,EobCheckNo,TotalBilled,TotalAllowed,TotalPaid,PatientResponsibility,PolicyNumber,IsAr);

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.tblAppointment') AND name='IX_tblAppointment_BillingPaymentGrid')
 CREATE INDEX IX_tblAppointment_BillingPaymentGrid
 ON dbo.tblAppointment(TenantId,OrgId,DOS,AppointmentId)
 INCLUDE(PatientId,OfficeId,PtTypeId,InsuranceName);

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.tblApptCodes') AND name='IX_tblApptCodes_BillingGrid')
 CREATE INDEX IX_tblApptCodes_BillingGrid
 ON dbo.tblApptCodes(TenantId,OrgId,AppointmentId)
 INCLUDE(ApptCodeId,ClaimId,ProcedureCodeId,ProcedureCode,TotalClaimAmount,ClaimAmount,InsuranceAmount,TypeId,Paid,PatientResponsibility,Balance,CarrierId,InsuranceCompanyId,PolicyNumber,PolicyNo,Modifiers);

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.tblClaimCodeAmountDetails') AND name='IX_tblClaimCodeAmountDetails_BillingGrid')
 CREATE INDEX IX_tblClaimCodeAmountDetails_BillingGrid
 ON dbo.tblClaimCodeAmountDetails(TenantId,ApptCodeId,IsClosed)
 INCLUDE(ClaimId,OrgId,PaidAmount,PatientResponsibility,Coinsurance,Deductible,EobCheckNo,EobStatusId);

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.tblClaimGrpCodeDetails') AND name='IX_tblClaimGrpCodeDetails_BillingGrid')
 CREATE INDEX IX_tblClaimGrpCodeDetails_BillingGrid
 ON dbo.tblClaimGrpCodeDetails(TenantId,OrgId,AppClaimId)
 INCLUDE(AppCodeId,CodeAmtId,Grp_Code,Grp_Amt);

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.tblClaimEobDocuments') AND name='IX_tblClaimEobDocuments_BillingGrid')
 CREATE INDEX IX_tblClaimEobDocuments_BillingGrid
 ON dbo.tblClaimEobDocuments(TenantId,ClaimId,CreatedDate DESC,Id DESC)
 INCLUDE(Attachment);
""");

        ApplyEmbeddedProcedure(migrationBuilder, "Accounts.Sql.StoredProcedures.usp_Billing_Claims_List.sql");
        ApplyEmbeddedProcedure(migrationBuilder, "Accounts.Sql.StoredProcedures.usp_Billing_Eob_List.sql");
    }

    private static void ApplyEmbeddedProcedure(MigrationBuilder migrationBuilder, string resourceName)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Missing embedded SQL resource: {resourceName}");
        using var reader = new StreamReader(stream);
        var sql = reader.ReadToEnd();
        foreach (var batch in System.Text.RegularExpressions.Regex.Split(sql, @"^\s*GO\s*$",
                     System.Text.RegularExpressions.RegexOptions.Multiline |
                     System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            if (!string.IsNullOrWhiteSpace(batch)) migrationBuilder.Sql(batch);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
DROP INDEX IF EXISTS IX_tblClaimEobDocuments_BillingGrid ON dbo.tblClaimEobDocuments;
DROP INDEX IF EXISTS IX_tblClaimGrpCodeDetails_BillingGrid ON dbo.tblClaimGrpCodeDetails;
DROP INDEX IF EXISTS IX_tblClaimCodeAmountDetails_BillingGrid ON dbo.tblClaimCodeAmountDetails;
DROP INDEX IF EXISTS IX_tblApptCodes_BillingGrid ON dbo.tblApptCodes;
DROP INDEX IF EXISTS IX_tblAppointment_BillingPaymentGrid ON dbo.tblAppointment;
DROP INDEX IF EXISTS IX_tblInsPTClaims_BillingClaimsGrid ON dbo.tblInsPTClaims;
""");
    }
}
