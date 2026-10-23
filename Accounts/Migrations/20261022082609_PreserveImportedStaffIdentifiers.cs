using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class PreserveImportedStaffIdentifiers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LegacyVacancyId",
                table: "Vacancies",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LegacyServiceStatusId",
                table: "StaffVacancy",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LegacyStaffId",
                table: "StaffVacancy",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LegacyVacancyId",
                table: "StaffVacancy",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Vacancies_TenantId_LegacyVacancyId",
                table: "Vacancies",
                columns: new[] { "TenantId", "LegacyVacancyId" },
                unique: true,
                filter: "[LegacyVacancyId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StaffVacancy_TenantId_LegacyStaffId",
                table: "StaffVacancy",
                columns: new[] { "TenantId", "LegacyStaffId" },
                unique: true,
                filter: "[LegacyStaffId] IS NOT NULL");
            migrationBuilder.Sql(StaffListProcedure);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS(SELECT 1 FROM dbo.StaffVacancy WHERE LegacyStaffId IS NOT NULL) OR EXISTS(SELECT 1 FROM dbo.Vacancies WHERE LegacyVacancyId IS NOT NULL) THROW 51000,'Imported IDs are populated; export/preserve them before rolling back this migration.',1;");
            migrationBuilder.Sql(StaffListProcedure
                .Replace("sv.LegacyStaffId,sv.LegacyVacancyId,sv.LegacyServiceStatusId,", "CAST(NULL AS INT) AS LegacyStaffId,CAST(NULL AS INT) AS LegacyVacancyId,CAST(NULL AS INT) AS LegacyServiceStatusId,")
                .Replace("CASE WHEN sv.LegacyStaffId IS NOT NULL THEN hr.JoiningDate\n            ELSE COALESCE(hr.JoiningDate,per.LastJoiningDate,per.CreatedDate) END", "COALESCE(hr.JoiningDate,per.LastJoiningDate,per.CreatedDate)"));
            migrationBuilder.DropIndex(
                name: "IX_Vacancies_TenantId_LegacyVacancyId",
                table: "Vacancies");

            migrationBuilder.DropIndex(
                name: "IX_StaffVacancy_TenantId_LegacyStaffId",
                table: "StaffVacancy");

            migrationBuilder.DropColumn(
                name: "LegacyVacancyId",
                table: "Vacancies");

            migrationBuilder.DropColumn(
                name: "LegacyServiceStatusId",
                table: "StaffVacancy");

            migrationBuilder.DropColumn(
                name: "LegacyStaffId",
                table: "StaffVacancy");

            migrationBuilder.DropColumn(
                name: "LegacyVacancyId",
                table: "StaffVacancy");
        }
    }
}
