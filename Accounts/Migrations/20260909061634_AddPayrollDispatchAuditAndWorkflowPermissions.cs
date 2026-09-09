using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class AddPayrollDispatchAuditAndWorkflowPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PaidByName",
                table: "PayrollRuns",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaidByUserId",
                table: "PayrollRuns",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PaidOnUtc",
                table: "PayrollRuns",
                type: "datetime2",
                nullable: true);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PaidByName",
                table: "PayrollRuns");

            migrationBuilder.DropColumn(
                name: "PaidByUserId",
                table: "PayrollRuns");

            migrationBuilder.DropColumn(
                name: "PaidOnUtc",
                table: "PayrollRuns");
        }
    }
}
