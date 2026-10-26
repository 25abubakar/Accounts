using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261026200000_ShowHistoricalPersonLoginNumber")]
public sealed class ShowHistoricalPersonLoginNumber : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DECLARE @definition nvarchar(max)=OBJECT_DEFINITION(OBJECT_ID(N'dbo.usp_Hr_Persons_List'));
        IF @definition IS NULL THROW 51000,'HR person list procedure is missing.',1;
        IF CHARINDEX(N'COALESCE(sv.LoginId, p.LastLoginId, N''-'') AS LoginId',@definition)>0 RETURN;
        IF CHARINDEX(N'COALESCE(sv.LoginId, N''-'') AS LoginId',@definition)=0
            THROW 51000,'Review the HR person-list login projection before migrating.',1;
        SET @definition=REPLACE(@definition,N'COALESCE(sv.LoginId, N''-'') AS LoginId',N'COALESCE(sv.LoginId, p.LastLoginId, N''-'') AS LoginId');
        DECLARE @header int=CHARINDEX(N'PROCEDURE',UPPER(@definition));
        IF @header=0 THROW 51000,'Review the HR procedure header before migrating.',1;
        SET @definition=N'CREATE OR ALTER '+SUBSTRING(@definition,@header,LEN(@definition));
        EXEC sys.sp_executesql @definition;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Historical person LT numbers must remain visible; restore the reviewed prior procedure to roll back.");
}
