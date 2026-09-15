using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260914230000_AddWorkflowAuthorityPinHash")]
public sealed class AddWorkflowAuthorityPinHash : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "PinHash", table: "ProcessActionAuthorities", type: "nvarchar(400)", maxLength: 400, nullable: true);
        migrationBuilder.AddColumn<DateTime>(name: "PinUpdatedOnUtc", table: "ProcessActionAuthorities", type: "datetime2", nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "PinHash", table: "ProcessActionAuthorities");
        migrationBuilder.DropColumn(name: "PinUpdatedOnUtc", table: "ProcessActionAuthorities");
    }
}
