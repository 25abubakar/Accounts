using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class AddAssessmentRecurringWindowAndPayrollAmount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsLocked",
                schema: "dbo",
                table: "StaffAssessments",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "SubmittedDateUtc",
                schema: "dbo",
                table: "StaffAssessments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AssessmentAmount",
                table: "PayrollLines",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<byte>(
                name: "CloseDay",
                schema: "dbo",
                table: "AssessmentSchedules",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)8);

            migrationBuilder.AddColumn<byte>(
                name: "CloseDay",
                schema: "dbo",
                table: "AssessmentBonusRules",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)8);

            migrationBuilder.AddColumn<byte>(
                name: "OpenDay",
                schema: "dbo",
                table: "AssessmentBonusRules",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)25);

            migrationBuilder.Sql(
                """
                UPDATE dbo.StaffAssessments
                SET IsLocked = 1,
                    SubmittedDateUtc = COALESCE(ModifiedDateUtc, CreatedDateUtc)
                WHERE Rating IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsLocked",
                schema: "dbo",
                table: "StaffAssessments");

            migrationBuilder.DropColumn(
                name: "SubmittedDateUtc",
                schema: "dbo",
                table: "StaffAssessments");

            migrationBuilder.DropColumn(
                name: "AssessmentAmount",
                table: "PayrollLines");

            migrationBuilder.DropColumn(
                name: "CloseDay",
                schema: "dbo",
                table: "AssessmentSchedules");

            migrationBuilder.DropColumn(
                name: "CloseDay",
                schema: "dbo",
                table: "AssessmentBonusRules");

            migrationBuilder.DropColumn(
                name: "OpenDay",
                schema: "dbo",
                table: "AssessmentBonusRules");
        }
    }
}
