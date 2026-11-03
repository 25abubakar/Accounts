using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261103130000_AlignOcrEobLegacyParity")]
public sealed class AlignOcrEobLegacyParity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
IF COL_LENGTH('dbo.tblEOBGrpCodeRules', 'Grp_Code') IS NULL
    ALTER TABLE dbo.tblEOBGrpCodeRules ADD Grp_Code nvarchar(50) NULL;
IF COL_LENGTH('dbo.tblEOBGrpCodeRules', 'Code') IS NULL
    ALTER TABLE dbo.tblEOBGrpCodeRules ADD Code nvarchar(50) NULL;
IF COL_LENGTH('dbo.tblEOBGrpCodeRules', 'Grp_Description') IS NULL
    ALTER TABLE dbo.tblEOBGrpCodeRules ADD Grp_Description nvarchar(1000) NULL;
IF COL_LENGTH('dbo.tblEOBGrpCodeRules', 'ColorId') IS NULL
    ALTER TABLE dbo.tblEOBGrpCodeRules ADD ColorId int NULL;
IF COL_LENGTH('dbo.tblEOBGrpCodeRules', 'ColorCode') IS NULL
    ALTER TABLE dbo.tblEOBGrpCodeRules ADD ColorCode nvarchar(50) NULL;
IF COL_LENGTH('dbo.tblEOBGrpCodeRules', 'Font_Color') IS NULL
    ALTER TABLE dbo.tblEOBGrpCodeRules ADD Font_Color nvarchar(50) NULL;
IF COL_LENGTH('dbo.tblEOBGrpCodeRules', 'Font_Size') IS NULL
    ALTER TABLE dbo.tblEOBGrpCodeRules ADD Font_Size nvarchar(20) NULL;
""");

        migrationBuilder.Sql("""
UPDATE dbo.tblEOBGrpCodeRules
SET Grp_Code = CASE WHEN CHARINDEX('-', ExplanationCodeKey) > 0
                    THEN LEFT(ExplanationCodeKey, CHARINDEX('-', ExplanationCodeKey) - 1)
                    ELSE ExplanationCodeKey END,
    Code = CASE WHEN CHARINDEX('-', ExplanationCodeKey) > 0
                THEN SUBSTRING(ExplanationCodeKey, CHARINDEX('-', ExplanationCodeKey) + 1, 50)
                ELSE ExplanationCodeKey END,
    Grp_Description = COALESCE(Grp_Description, Description)
WHERE ExplanationCodeKey IS NOT NULL
  AND (Grp_Code IS NULL OR Code IS NULL OR Grp_Description IS NULL);
""");

        migrationBuilder.Sql("""
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.tblEOBGrpCodeRules') AND name = 'IX_tblEOBGrpCodeRules_OrgId_Grp_Code_Code')
    CREATE UNIQUE INDEX IX_tblEOBGrpCodeRules_OrgId_Grp_Code_Code
        ON dbo.tblEOBGrpCodeRules(OrgId, Grp_Code, Code)
        WHERE OrgId IS NOT NULL AND Grp_Code IS NOT NULL AND Code IS NOT NULL;
""");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.tblEOBGrpCodeRules') AND name = 'IX_tblEOBGrpCodeRules_OrgId_Grp_Code_Code')
    DROP INDEX IX_tblEOBGrpCodeRules_OrgId_Grp_Code_Code ON dbo.tblEOBGrpCodeRules;
IF COL_LENGTH('dbo.tblEOBGrpCodeRules', 'Font_Size') IS NOT NULL ALTER TABLE dbo.tblEOBGrpCodeRules DROP COLUMN Font_Size;
IF COL_LENGTH('dbo.tblEOBGrpCodeRules', 'Font_Color') IS NOT NULL ALTER TABLE dbo.tblEOBGrpCodeRules DROP COLUMN Font_Color;
IF COL_LENGTH('dbo.tblEOBGrpCodeRules', 'ColorCode') IS NOT NULL ALTER TABLE dbo.tblEOBGrpCodeRules DROP COLUMN ColorCode;
IF COL_LENGTH('dbo.tblEOBGrpCodeRules', 'ColorId') IS NOT NULL ALTER TABLE dbo.tblEOBGrpCodeRules DROP COLUMN ColorId;
IF COL_LENGTH('dbo.tblEOBGrpCodeRules', 'Grp_Description') IS NOT NULL ALTER TABLE dbo.tblEOBGrpCodeRules DROP COLUMN Grp_Description;
IF COL_LENGTH('dbo.tblEOBGrpCodeRules', 'Code') IS NOT NULL ALTER TABLE dbo.tblEOBGrpCodeRules DROP COLUMN Code;
IF COL_LENGTH('dbo.tblEOBGrpCodeRules', 'Grp_Code') IS NOT NULL ALTER TABLE dbo.tblEOBGrpCodeRules DROP COLUMN Grp_Code;
""");
    }
}
