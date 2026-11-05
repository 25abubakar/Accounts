using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Reflection;

#nullable disable
namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261103170000_CompleteClaimsLegacyParity")]
public sealed class CompleteClaimsLegacyParity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
IF COL_LENGTH('dbo.tblApptCodes','TenantId') IS NULL ALTER TABLE dbo.tblApptCodes ADD TenantId int NULL;
IF COL_LENGTH('dbo.tblApptCodes','Unit') IS NULL ALTER TABLE dbo.tblApptCodes ADD Unit int NULL;
IF COL_LENGTH('dbo.tblApptCodes','TypeId') IS NULL ALTER TABLE dbo.tblApptCodes ADD TypeId int NULL;
IF COL_LENGTH('dbo.tblApptCodes','Remarks') IS NULL ALTER TABLE dbo.tblApptCodes ADD Remarks nvarchar(1000) NULL;
IF COL_LENGTH('dbo.tblInsPTClaims','TenantId') IS NULL ALTER TABLE dbo.tblInsPTClaims ADD TenantId int NULL;
IF COL_LENGTH('dbo.tblClaimEobDocuments','TenantId') IS NULL ALTER TABLE dbo.tblClaimEobDocuments ADD TenantId int NULL;
IF COL_LENGTH('dbo.tblClaimsLog','TenantId') IS NULL ALTER TABLE dbo.tblClaimsLog ADD TenantId int NULL;
IF COL_LENGTH('dbo.tblCarrier','TenantId') IS NULL ALTER TABLE dbo.tblCarrier ADD TenantId int NULL;
IF COL_LENGTH('dbo.tblClaimCodeAmountDetails','TenantId') IS NULL ALTER TABLE dbo.tblClaimCodeAmountDetails ADD TenantId int NULL;
IF COL_LENGTH('dbo.tblClaimCodeAmountDetails','Coinsurance') IS NULL ALTER TABLE dbo.tblClaimCodeAmountDetails ADD Coinsurance decimal(18,2) NULL;
IF COL_LENGTH('dbo.tblClaimCodeAmountDetails','Deductible') IS NULL ALTER TABLE dbo.tblClaimCodeAmountDetails ADD Deductible decimal(18,2) NULL;
IF COL_LENGTH('dbo.tblClaimCodeAmountDetails','AllowedAmount') IS NULL ALTER TABLE dbo.tblClaimCodeAmountDetails ADD AllowedAmount decimal(18,2) NULL;
IF COL_LENGTH('dbo.tblClaimCodeAmountDetails','LoadDate') IS NULL ALTER TABLE dbo.tblClaimCodeAmountDetails ADD LoadDate nvarchar(100) NULL;
IF COL_LENGTH('dbo.tblClaimCodeAmountDetails','EobStatusId') IS NULL ALTER TABLE dbo.tblClaimCodeAmountDetails ADD EobStatusId int NULL;
IF OBJECT_ID('dbo.tblClaimGrpCodeDetails','U') IS NULL
 CREATE TABLE dbo.tblClaimGrpCodeDetails(Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblClaimGrpCodeDetails PRIMARY KEY,TenantId int NULL,OrgId int NULL,AppClaimId int NULL,AppCodeId int NULL,CodeAmtId int NULL,Grp_Code nvarchar(50) NULL,Grp_Amt decimal(18,2) NULL,CreatedDate datetime2 NULL);
IF OBJECT_ID('dbo.tblPatientApptLog','U') IS NULL
 CREATE TABLE dbo.tblPatientApptLog(Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblPatientApptLog PRIMARY KEY,TenantId int NULL,ApptId int NULL,ProcessDate datetime2 NULL,StatusId int NULL,Remarks nvarchar(1000) NULL,CreatedBy nvarchar(450) NULL,CreatedDate datetime2 NULL);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.tblInsPTClaims') AND name='IX_tblInsPTClaims_Tenant_Date') CREATE INDEX IX_tblInsPTClaims_Tenant_Date ON dbo.tblInsPTClaims(TenantId,CreatedDate) INCLUDE(AppointmentId,PatientId,CarrierId,StatusId);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.tblApptCodes') AND name='IX_tblApptCodes_Tenant_Claim') CREATE INDEX IX_tblApptCodes_Tenant_Claim ON dbo.tblApptCodes(TenantId,ClaimId) INCLUDE(ApptCodeId,ProcedureCode,Modifiers,TotalClaimAmount,ClaimAmount,InsuranceAmount,EobAllowed);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.tblClaimCodeAmountDetails') AND name='IX_tblClaimCodeAmountDetails_Tenant_Claim_Code') CREATE INDEX IX_tblClaimCodeAmountDetails_Tenant_Claim_Code ON dbo.tblClaimCodeAmountDetails(TenantId,ClaimId,ApptCodeId) INCLUDE(Id,PaidAmount,PatientResponsibility,EobCheckNo,IsClosed);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.tblClaimGrpCodeDetails') AND name='UX_tblClaimGrpCodeDetails_Link_Code')
 AND NOT EXISTS(SELECT 1 FROM dbo.tblClaimGrpCodeDetails WHERE TenantId IS NOT NULL AND AppClaimId IS NOT NULL AND AppCodeId IS NOT NULL AND CodeAmtId IS NOT NULL AND Grp_Code IS NOT NULL GROUP BY TenantId,AppClaimId,AppCodeId,CodeAmtId,Grp_Code HAVING COUNT(*)>1)
 CREATE UNIQUE INDEX UX_tblClaimGrpCodeDetails_Link_Code ON dbo.tblClaimGrpCodeDetails(TenantId,AppClaimId,AppCodeId,CodeAmtId,Grp_Code) WHERE TenantId IS NOT NULL AND AppClaimId IS NOT NULL AND AppCodeId IS NOT NULL AND CodeAmtId IS NOT NULL AND Grp_Code IS NOT NULL;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.tblClaimGrpCodeDetails') AND name='IX_tblClaimGrpCodeDetails_Claim') CREATE INDEX IX_tblClaimGrpCodeDetails_Claim ON dbo.tblClaimGrpCodeDetails(TenantId,AppClaimId,AppCodeId,CodeAmtId) INCLUDE(Grp_Code,Grp_Amt);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.tblInsPTClaims') AND name='IX_tblInsPTClaims_Tenant_Appointment') CREATE INDEX IX_tblInsPTClaims_Tenant_Appointment ON dbo.tblInsPTClaims(TenantId,AppointmentId) WHERE TenantId IS NOT NULL AND AppointmentId IS NOT NULL;
""");
        ApplyEmbeddedProcedure(migrationBuilder, "Accounts.Sql.StoredProcedures.usp_Billing_Claims_List.sql");
        ApplyEmbeddedProcedure(migrationBuilder, "Accounts.Sql.StoredProcedures.usp_Billing_ClaimCodeDetails_List.sql");
        ApplyEmbeddedProcedure(migrationBuilder, "Accounts.Sql.StoredProcedures.usp_Billing_Eob_List.sql");
    }

    private static void ApplyEmbeddedProcedure(MigrationBuilder migrationBuilder, string resourceName)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Missing embedded SQL resource: {resourceName}");
        using var reader = new StreamReader(stream);
        var sql = reader.ReadToEnd();
        foreach (var batch in System.Text.RegularExpressions.Regex.Split(sql, @"^\s*GO\s*$", System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            if (!string.IsNullOrWhiteSpace(batch)) migrationBuilder.Sql(batch);
    }

    // Financial group-code history is intentionally retained on rollback.
    protected override void Down(MigrationBuilder migrationBuilder) { }
}
