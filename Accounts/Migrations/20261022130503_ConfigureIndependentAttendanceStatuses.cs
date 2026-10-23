using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class ConfigureIndependentAttendanceStatuses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(SeedSql);
            // Dynamic execution keeps CREATE OR ALTER PROCEDURE the first statement
            // in its batch, including when EF generates an idempotent deployment script.
            migrationBuilder.Sql("EXEC(N'" + AttendanceReportSql.Replace("'", "''") + "');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("EXEC(N'" + PreviousReportSql.Replace("'", "''") + "');");
            // Keep Settings data: IDs may already be referenced by attendance or other
            // workflows. Rolling back a report must not delete shared configuration.
        }
    }
}
