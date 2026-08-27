using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class AddPayScaleRegistrationFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ApplicableType",
                table: "SalaryScales",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ApplyAfter",
                table: "SalaryScales",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "IncrementMonth",
                table: "SalaryScales",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RuleRegistrationId",
                table: "SalaryScales",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalaryScales_RuleRegistrationId",
                table: "SalaryScales",
                column: "RuleRegistrationId");

            migrationBuilder.AddForeignKey(
                name: "FK_SalaryScales_PayScaleRuleRegistrations_RuleRegistrationId",
                table: "SalaryScales",
                column: "RuleRegistrationId",
                principalTable: "PayScaleRuleRegistrations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SalaryScales_PayScaleRuleRegistrations_RuleRegistrationId",
                table: "SalaryScales");

            migrationBuilder.DropIndex(
                name: "IX_SalaryScales_RuleRegistrationId",
                table: "SalaryScales");

            migrationBuilder.DropColumn(
                name: "ApplicableType",
                table: "SalaryScales");

            migrationBuilder.DropColumn(
                name: "ApplyAfter",
                table: "SalaryScales");

            migrationBuilder.DropColumn(
                name: "IncrementMonth",
                table: "SalaryScales");

            migrationBuilder.DropColumn(
                name: "RuleRegistrationId",
                table: "SalaryScales");
        }
    }
}
