using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

/// <summary>
/// Repairs the payroll generator installed by an earlier migration. AppLookupValues
/// uses LookupValueId as its key; referencing Id prevents SQL Server from compiling
/// the allowance/shift section and produces a secondary STRING_SPLIT void-type error.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260916120000_RepairPayrollGenerateLookupKey")]
public sealed class RepairPayrollGenerateLookupKey : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DECLARE @definition nvarchar(max) = OBJECT_DEFINITION(OBJECT_ID(N'dbo.usp_Payroll_GenerateMonthly'));

            IF @definition IS NULL
                THROW 51020, 'dbo.usp_Payroll_GenerateMonthly is missing. Apply the payroll generation migration first.', 1;

            IF CHARINDEX(N'WHERE lv.LookupValueId = a.ShiftLookupValueId', @definition) = 0
            BEGIN
                IF CHARINDEX(N'WHERE lv.Id = a.ShiftLookupValueId', @definition) = 0
                    THROW 51021, 'The payroll procedure lookup-key pattern was not found; repair was not applied.', 1;

                SET @definition = REPLACE(
                    @definition,
                    N'WHERE lv.Id = a.ShiftLookupValueId',
                    N'WHERE lv.LookupValueId = a.ShiftLookupValueId');

                DECLARE @procedureKeyword int = CHARINDEX(N'PROCEDURE', UPPER(@definition));
                IF @procedureKeyword = 0
                    THROW 51022, 'The payroll procedure definition could not be normalized for ALTER.', 1;
                SET @definition = N'ALTER ' + SUBSTRING(@definition, @procedureKeyword, LEN(@definition));

                EXEC sys.sp_executesql @definition;
            END;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DECLARE @definition nvarchar(max) = OBJECT_DEFINITION(OBJECT_ID(N'dbo.usp_Payroll_GenerateMonthly'));

            IF @definition IS NOT NULL
               AND CHARINDEX(N'WHERE lv.LookupValueId = a.ShiftLookupValueId', @definition) > 0
            BEGIN
                SET @definition = REPLACE(
                    @definition,
                    N'WHERE lv.LookupValueId = a.ShiftLookupValueId',
                    N'WHERE lv.Id = a.ShiftLookupValueId');

                DECLARE @procedureKeyword int = CHARINDEX(N'PROCEDURE', UPPER(@definition));
                IF @procedureKeyword = 0
                    THROW 51022, 'The payroll procedure definition could not be normalized for ALTER.', 1;
                SET @definition = N'ALTER ' + SUBSTRING(@definition, @procedureKeyword, LEN(@definition));

                EXEC sys.sp_executesql @definition;
            END;
            """);
    }
}
