using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

/// <summary>
/// Payment/Receipt list SPs now return the AccountsProjects name for Project.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20261016120000_JoinRoznamchaListProjectName")]
public sealed class JoinRoznamchaListProjectName : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(RoznamchaListSql.Payment);
        migrationBuilder.Sql(RoznamchaListSql.Receipt);
        migrationBuilder.Sql(RoznamchaListSql.Unified);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(RoznamchaListSql.Payment);
        migrationBuilder.Sql(RoznamchaListSql.Receipt);
        migrationBuilder.Sql(RoznamchaListSql.Unified);
    }
}
