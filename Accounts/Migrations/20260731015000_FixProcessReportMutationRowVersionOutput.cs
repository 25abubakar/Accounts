using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260731015000_FixProcessReportMutationRowVersionOutput")]
public sealed class FixProcessReportMutationRowVersionOutput : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // These procedures were already installed before the corrected source migrations
        // existed. Patch only their row-version projection while preserving the deployed,
        // database-owned workflow business logic.
        migrationBuilder.Sql(
            """
            DECLARE @ProcedureName sysname,
                    @Definition nvarchar(max),
                    @ProcedureKeyword int;

            DECLARE procedure_cursor CURSOR LOCAL FAST_FORWARD FOR
                SELECT name
                FROM (VALUES
                    (N'usp_ProcessReport_Submit'),
                    (N'usp_ProcessReport_Action')
                ) procedures(name);

            OPEN procedure_cursor;
            FETCH NEXT FROM procedure_cursor INTO @ProcedureName;
            WHILE @@FETCH_STATUS = 0
            BEGIN
                SET @Definition=OBJECT_DEFINITION(
                    OBJECT_ID(QUOTENAME(N'dbo')+N'.'+QUOTENAME(@ProcedureName),N'P'));

                IF @Definition IS NULL
                    THROW 51239, 'A required process workflow procedure is missing.', 1;

                SET @Definition=REPLACE(
                    @Definition,
                    N'CONVERT(varchar(24),r.RowVersion,2)',
                    N'CONVERT(varchar(16),CONVERT(varbinary(8),r.RowVersion),2)');
                SET @Definition=REPLACE(
                    @Definition,
                    N'CONVERT(varchar(24),RowVersion,2)',
                    N'CONVERT(varchar(16),CONVERT(varbinary(8),RowVersion),2)');

                SET @ProcedureKeyword=CHARINDEX(N'PROCEDURE',UPPER(@Definition));
                IF @ProcedureKeyword=0
                    THROW 51240, 'The process workflow procedure definition is invalid.', 1;

                SET @Definition=STUFF(@Definition,1,@ProcedureKeyword-1,N'ALTER ');
                EXEC sys.sp_executesql @Definition;

                FETCH NEXT FROM procedure_cursor INTO @ProcedureName;
            END;
            CLOSE procedure_cursor;
            DEALLOCATE procedure_cursor;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Keep safe hexadecimal serialization on rollback.
    }
}
