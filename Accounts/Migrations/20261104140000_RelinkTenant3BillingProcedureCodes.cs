using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261104140000_RelinkTenant3BillingProcedureCodes")]
public sealed class RelinkTenant3BillingProcedureCodes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
;WITH SourceCodes AS
(
    SELECT UPPER(LTRIM(RTRIM(ProcedureCode))) NormalizedCode,
           MIN(LTRIM(RTRIM(ProcedureCode))) ProcedureCode,
           MAX(ClaimAmount) StandardRate
    FROM dbo.tblApptCodes
    WHERE TenantId=3 AND NULLIF(LTRIM(RTRIM(ProcedureCode)),N'') IS NOT NULL
    GROUP BY UPPER(LTRIM(RTRIM(ProcedureCode)))
)
INSERT INTO dbo.tblProcedureCodes(TenantId,Codes,StandardRate)
SELECT 3,source.ProcedureCode,source.StandardRate
FROM SourceCodes source
WHERE NOT EXISTS
(
    SELECT 1 FROM dbo.tblProcedureCodes existing
    WHERE (existing.TenantId=3 OR existing.TenantId IS NULL)
      AND UPPER(LTRIM(RTRIM(existing.Codes)))=source.NormalizedCode
);

UPDATE apptCode
SET ProcedureCodeId=resolved.ProcCodeId
FROM dbo.tblApptCodes apptCode
CROSS APPLY
(
    SELECT TOP (1) procedureCode.ProcCodeId
    FROM dbo.tblProcedureCodes procedureCode
    WHERE (procedureCode.TenantId=3 OR procedureCode.TenantId IS NULL)
      AND UPPER(LTRIM(RTRIM(procedureCode.Codes)))=UPPER(LTRIM(RTRIM(apptCode.ProcedureCode)))
    ORDER BY CASE WHEN procedureCode.TenantId=3 THEN 0 ELSE 1 END,procedureCode.ProcCodeId
) resolved
WHERE apptCode.TenantId=3
  AND NULLIF(LTRIM(RTRIM(apptCode.ProcedureCode)),N'') IS NOT NULL
  AND (apptCode.ProcedureCodeId IS NULL OR apptCode.ProcedureCodeId<>resolved.ProcCodeId);
""");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Imported procedure master/link corrections are intentionally retained.
    }
}
