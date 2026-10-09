using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261009090000_ClarifySaleRoznamchaQuantityBalances")]
public sealed class ClarifySaleRoznamchaQuantityBalances : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
        => migrationBuilder.Sql(SaleRoznamchaSql.DailySaleListProcedure);

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.usp_SaleRoznamcha_DailySaleList;");
}
