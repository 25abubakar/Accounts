using Microsoft.EntityFrameworkCore.Migrations;

using Accounts.Data;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class AddSaleRoznamchaStatusBadgeColors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ColorCode",
                table: "SaleRoznamchaSaleStatuses",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ColorCode",
                table: "SaleRoznamchaPaidStatuses",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.Sql(ExecutableBatch(SaleRoznamchaSql.SettingsListWithColorsProcedure));
            migrationBuilder.Sql(ExecutableBatch(SaleRoznamchaSql.SettingsSaveWithColorsProcedure));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(ExecutableBatch(SaleRoznamchaSql.SettingsListProcedure));
            migrationBuilder.Sql(ExecutableBatch(SaleRoznamchaSql.SettingsSaveProcedure));

            migrationBuilder.DropColumn(
                name: "ColorCode",
                table: "SaleRoznamchaSaleStatuses");

            migrationBuilder.DropColumn(
                name: "ColorCode",
                table: "SaleRoznamchaPaidStatuses");
        }

        private static string ExecutableBatch(string sql) => $"EXEC(N'{sql.Replace("'", "''")}');";
    }
}
