using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class LinkAttendanceCanonicalStatuses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CanonicalStatusId",
                table: "AttendanceRecords",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRecords_CanonicalStatusId",
                table: "AttendanceRecords",
                column: "CanonicalStatusId");

            migrationBuilder.AddForeignKey(
                name: "FK_AttendanceRecords_Statuses_CanonicalStatusId",
                table: "AttendanceRecords",
                column: "CanonicalStatusId",
                principalSchema: "PlatformSettings",
                principalTable: "Statuses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql(CanonicalReportSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS(SELECT 1 FROM dbo.AttendanceRecords WHERE CanonicalStatusId IS NOT NULL) THROW 51000,'Preserve canonical attendance statuses before rollback.',1;");
            migrationBuilder.Sql(LegacyReportSql);
            migrationBuilder.DropForeignKey(
                name: "FK_AttendanceRecords_Statuses_CanonicalStatusId",
                table: "AttendanceRecords");

            migrationBuilder.DropIndex(
                name: "IX_AttendanceRecords_CanonicalStatusId",
                table: "AttendanceRecords");

            migrationBuilder.DropColumn(
                name: "CanonicalStatusId",
                table: "AttendanceRecords");
        }
    }
}
