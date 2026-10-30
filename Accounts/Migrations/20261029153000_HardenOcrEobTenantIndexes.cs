using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261029153000_HardenOcrEobTenantIndexes")]
public sealed class HardenOcrEobTenantIndexes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF EXISTS (
                SELECT 1
                FROM dbo.tblUploadedOcrEobRecords
                WHERE OrgId IS NOT NULL AND Source IS NOT NULL AND Check_EFT IS NOT NULL AND Check_EFT <> N''
                GROUP BY OrgId, Source, Check_EFT
                HAVING COUNT_BIG(*) > 1)
                THROW 51010, 'Duplicate OCR tenant/source/check records must be reconciled before applying the OCR uniqueness migration.', 1;

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.tblUploadedOcrEobRecords') AND name = N'IX_tblUploadedOcrEobRecords_OrgId_Source_Check_EFT')
                CREATE UNIQUE INDEX IX_tblUploadedOcrEobRecords_OrgId_Source_Check_EFT
                ON dbo.tblUploadedOcrEobRecords(OrgId, Source, Check_EFT)
                WHERE OrgId IS NOT NULL AND Source IS NOT NULL AND Check_EFT IS NOT NULL AND Check_EFT <> N'';

            IF EXISTS (
                SELECT 1
                FROM dbo.tblEOBGrpCodeRules
                WHERE OrgId IS NOT NULL AND [Type] IS NOT NULL AND ExplanationCodeKey IS NOT NULL
                GROUP BY OrgId, [Type], ExplanationCodeKey
                HAVING COUNT_BIG(*) > 1)
                THROW 51011, 'Duplicate OCR explanation-code rules must be reconciled before applying the OCR uniqueness migration.', 1;

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.tblEOBGrpCodeRules') AND name = N'IX_tblEOBGrpCodeRules_OrgId_Type_ExplanationCodeKey')
                CREATE UNIQUE INDEX IX_tblEOBGrpCodeRules_OrgId_Type_ExplanationCodeKey
                ON dbo.tblEOBGrpCodeRules(OrgId, [Type], ExplanationCodeKey)
                WHERE OrgId IS NOT NULL AND [Type] IS NOT NULL AND ExplanationCodeKey IS NOT NULL;

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.tblOCRDocHeaderInfo') AND name = N'IX_tblOCRDocHeaderInfo_OrgId_EobAttId')
                CREATE INDEX IX_tblOCRDocHeaderInfo_OrgId_EobAttId ON dbo.tblOCRDocHeaderInfo(OrgId, EobAttId);

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.tblOcrPatientDetail') AND name = N'IX_tblOcrPatientDetail_OrgId_OcrHeaderId')
                CREATE INDEX IX_tblOcrPatientDetail_OrgId_OcrHeaderId ON dbo.tblOcrPatientDetail(OrgId, OcrHeaderId);

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.tblOcrProcedureDetails') AND name = N'IX_tblOcrProcedureDetails_OrgId_OcrPatientId')
                CREATE INDEX IX_tblOcrProcedureDetails_OrgId_OcrPatientId ON dbo.tblOcrProcedureDetails(OrgId, OcrPatientId);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP INDEX IF EXISTS IX_tblOcrProcedureDetails_OrgId_OcrPatientId ON dbo.tblOcrProcedureDetails;
            DROP INDEX IF EXISTS IX_tblOcrPatientDetail_OrgId_OcrHeaderId ON dbo.tblOcrPatientDetail;
            DROP INDEX IF EXISTS IX_tblOCRDocHeaderInfo_OrgId_EobAttId ON dbo.tblOCRDocHeaderInfo;
            DROP INDEX IF EXISTS IX_tblEOBGrpCodeRules_OrgId_Type_ExplanationCodeKey ON dbo.tblEOBGrpCodeRules;
            DROP INDEX IF EXISTS IX_tblUploadedOcrEobRecords_OrgId_Source_Check_EFT ON dbo.tblUploadedOcrEobRecords;
            """);
    }
}
