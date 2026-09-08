using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

/// <summary>
/// Extends PayrollTaxSlabs for legacy Tax Rules fields: Slabs name, RateType, TotTax.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260908140000_ExtendPayrollTaxSlabsForTaxRules")]
public sealed class ExtendPayrollTaxSlabsForTaxRules : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF COL_LENGTH(N'dbo.PayrollTaxSlabs', N'SlabName') IS NULL
                ALTER TABLE dbo.PayrollTaxSlabs ADD SlabName nvarchar(80) NOT NULL CONSTRAINT DF_PayrollTaxSlabs_SlabName DEFAULT (N'');

            IF COL_LENGTH(N'dbo.PayrollTaxSlabs', N'RateType') IS NULL
                ALTER TABLE dbo.PayrollTaxSlabs ADD RateType nvarchar(80) NULL;

            IF COL_LENGTH(N'dbo.PayrollTaxSlabs', N'TotTax') IS NULL
                ALTER TABLE dbo.PayrollTaxSlabs ADD TotTax decimal(18,2) NULL;
            """);

        migrationBuilder.Sql(
            """
            EXEC(N'
            UPDATE dbo.PayrollTaxSlabs
            SET SlabName = CONCAT(N''Slab '', CAST(Id AS nvarchar(20)))
            WHERE SlabName IS NULL OR LTRIM(RTRIM(SlabName)) = N'''';
            ');
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF COL_LENGTH(N'dbo.PayrollTaxSlabs', N'TotTax') IS NOT NULL
                ALTER TABLE dbo.PayrollTaxSlabs DROP COLUMN TotTax;
            IF COL_LENGTH(N'dbo.PayrollTaxSlabs', N'RateType') IS NOT NULL
                ALTER TABLE dbo.PayrollTaxSlabs DROP COLUMN RateType;
            IF COL_LENGTH(N'dbo.PayrollTaxSlabs', N'SlabName') IS NOT NULL
            BEGIN
                DECLARE @df sysname;
                SELECT @df = dc.name
                FROM sys.default_constraints dc
                INNER JOIN sys.columns c ON c.default_object_id = dc.object_id
                WHERE dc.parent_object_id = OBJECT_ID(N'dbo.PayrollTaxSlabs') AND c.name = N'SlabName';
                IF @df IS NOT NULL EXEC(N'ALTER TABLE dbo.PayrollTaxSlabs DROP CONSTRAINT [' + @df + N']');
                ALTER TABLE dbo.PayrollTaxSlabs DROP COLUMN SlabName;
            END
            """);
    }
}
