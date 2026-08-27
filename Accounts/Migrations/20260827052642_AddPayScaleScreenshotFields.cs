using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class AddPayScaleScreenshotFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContractType",
                table: "SalaryScales",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CurrentPay",
                table: "SalaryScales",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "FrequencyType",
                table: "SalaryScales",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "Regular");

            migrationBuilder.AddColumn<string>(
                name: "RateType",
                table: "SalaryScales",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "PM");

            migrationBuilder.AddColumn<DateTime>(
                name: "DateFrom",
                table: "PayRules",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DateTo",
                table: "PayRules",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RuleType",
                table: "PayRules",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "Standard");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContractType",
                table: "SalaryScales");

            migrationBuilder.DropColumn(
                name: "CurrentPay",
                table: "SalaryScales");

            migrationBuilder.DropColumn(
                name: "FrequencyType",
                table: "SalaryScales");

            migrationBuilder.DropColumn(
                name: "RateType",
                table: "SalaryScales");

            migrationBuilder.DropColumn(
                name: "DateFrom",
                table: "PayRules");

            migrationBuilder.DropColumn(
                name: "DateTo",
                table: "PayRules");

            migrationBuilder.DropColumn(
                name: "RuleType",
                table: "PayRules");
        }
    }
}
