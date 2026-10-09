using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261008170000_AddSaleRoznamchaHistoryReport")]
public sealed class AddSaleRoznamchaHistoryReport : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
        => migrationBuilder.Sql(SaleRoznamchaSql.HistoryAggregateProcedure);

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.usp_SaleRoznamcha_HistoryAggregate;");
}
