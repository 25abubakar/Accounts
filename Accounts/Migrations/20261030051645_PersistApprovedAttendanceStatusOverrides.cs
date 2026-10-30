using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class PersistApprovedAttendanceStatusOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ApprovedStatusOverrideByUserId",
                table: "AttendanceRecords",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedStatusOverrideDateUtc",
                table: "AttendanceRecords",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ApprovedStatusOverrideId",
                table: "AttendanceRecords",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ApprovedStatusOverrideReportId",
                table: "AttendanceRecords",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRecords_ApprovedStatusOverrideId",
                table: "AttendanceRecords",
                column: "ApprovedStatusOverrideId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRecords_ApprovedStatusOverrideReportId",
                table: "AttendanceRecords",
                column: "ApprovedStatusOverrideReportId");

            migrationBuilder.AddForeignKey(
                name: "FK_AttendanceRecords_PlatformSettingStatuses_ApprovedStatusOverrideId",
                table: "AttendanceRecords",
                column: "ApprovedStatusOverrideId",
                principalSchema: "PlatformSettings",
                principalTable: "Statuses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AttendanceRecords_PlatformSettingStatuses_ApprovedStatusOverrideId",
                table: "AttendanceRecords");

            migrationBuilder.DropIndex(
                name: "IX_AttendanceRecords_ApprovedStatusOverrideId",
                table: "AttendanceRecords");

            migrationBuilder.DropIndex(
                name: "IX_AttendanceRecords_ApprovedStatusOverrideReportId",
                table: "AttendanceRecords");

            migrationBuilder.DropColumn(
                name: "ApprovedStatusOverrideByUserId",
                table: "AttendanceRecords");

            migrationBuilder.DropColumn(
                name: "ApprovedStatusOverrideDateUtc",
                table: "AttendanceRecords");

            migrationBuilder.DropColumn(
                name: "ApprovedStatusOverrideId",
                table: "AttendanceRecords");

            migrationBuilder.DropColumn(
                name: "ApprovedStatusOverrideReportId",
                table: "AttendanceRecords");

        }
    }
}
