using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261104202000_AllowLegacyClaimGroupDuplicates")]
public sealed class AllowLegacyClaimGroupDuplicates : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.tblClaimGrpCodeDetails') AND name='UX_tblClaimGrpCodeDetails_Link_Code')
    DROP INDEX UX_tblClaimGrpCodeDetails_Link_Code ON dbo.tblClaimGrpCodeDetails;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.tblClaimGrpCodeDetails') AND name='IX_tblClaimGrpCodeDetails_LegacyLookup')
    CREATE INDEX IX_tblClaimGrpCodeDetails_LegacyLookup ON dbo.tblClaimGrpCodeDetails(TenantId,AppClaimId,AppCodeId,CodeAmtId,Grp_Code);
""");

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
DROP INDEX IF EXISTS IX_tblClaimGrpCodeDetails_LegacyLookup ON dbo.tblClaimGrpCodeDetails;
""");
}
