using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class RefactorStaffVacancyAndPersons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── Drop FKs that depend on Staff ───────────────────────────────────
            migrationBuilder.DropForeignKey(
                name: "FK_DepartmentAccessMatrix_Staff_StaffId",
                table: "DepartmentAccessMatrix");

            migrationBuilder.DropForeignKey(
                name: "FK_StaffAccessGroups_Staff_StaffId",
                table: "StaffAccessGroups");

            migrationBuilder.DropForeignKey(
                name: "FK_UserPermissionOverrides_Staff_StaffId",
                table: "UserPermissionOverrides");

            // ── Drop FKs on Staff itself (we'll recreate with new names) ───────
            migrationBuilder.DropForeignKey(
                name: "FK_Staff_Persons_PersonId",
                table: "Staff");

            migrationBuilder.DropForeignKey(
                name: "FK_Staff_Vacancies_VacancyId",
                table: "Staff");

            // ── Rename table Staff → StaffVacancy ──────────────────────────────
            migrationBuilder.RenameTable(
                name: "Staff",
                newName: "StaffVacancy");

            // Rename PK to match new table name (optional but keeps schema tidy)
            migrationBuilder.DropPrimaryKey(name: "PK_Staff", table: "StaffVacancy");
            migrationBuilder.AddPrimaryKey(name: "PK_StaffVacancy", table: "StaffVacancy", column: "StaffId");

            // Drop old indexes (they still exist under old names after rename)
            migrationBuilder.DropIndex(name: "IX_Staff_PersonId", table: "StaffVacancy");
            migrationBuilder.DropIndex(name: "IX_Staff_VacancyId", table: "StaffVacancy");

            // ── Add LoginId to StaffVacancy (moved from Persons) ───────────────
            migrationBuilder.AddColumn<string>(
                name: "LoginId",
                table: "StaffVacancy",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            // ── Move Persons.LoginId → StaffVacancy.LoginId (only for hired persons) ──
            migrationBuilder.Sql(@"
UPDATE sv
SET sv.LoginId = p.LoginId
FROM StaffVacancy sv
INNER JOIN Persons p ON p.PersonId = sv.PersonId
WHERE sv.LoginId IS NULL;
");

            // ── Remove staff profile columns; keep only StaffId/VacancyId/PersonId/LoginId ──
            migrationBuilder.DropColumn(name: "Email",      table: "StaffVacancy");
            migrationBuilder.DropColumn(name: "FullName",   table: "StaffVacancy");
            migrationBuilder.DropColumn(name: "JoiningDate",table: "StaffVacancy");
            migrationBuilder.DropColumn(name: "Phone",      table: "StaffVacancy");
            migrationBuilder.DropColumn(name: "PhotoUrl",   table: "StaffVacancy");

            // ── Persons: drop BranchId + LoginId, add PersonalEmail ────────────
            migrationBuilder.DropIndex(
                name: "IX_Persons_LoginId",
                table: "Persons");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "Persons");

            migrationBuilder.DropColumn(
                name: "LoginId",
                table: "Persons");

            migrationBuilder.AddColumn<string>(
                name: "PersonalEmail",
                table: "Persons",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            // ── Rebuild indexes on StaffVacancy ───────────────────────────────
            migrationBuilder.CreateIndex(
                name: "IX_StaffVacancy_LoginId",
                table: "StaffVacancy",
                column: "LoginId",
                unique: true,
                filter: "[LoginId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StaffVacancy_PersonId",
                table: "StaffVacancy",
                column: "PersonId",
                unique: true,
                filter: "[PersonId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StaffVacancy_VacancyId",
                table: "StaffVacancy",
                column: "VacancyId",
                unique: true,
                filter: "[VacancyId] IS NOT NULL");

            // ── Recreate FKs with new names ───────────────────────────────────
            migrationBuilder.AddForeignKey(
                name: "FK_StaffVacancy_Persons_PersonId",
                table: "StaffVacancy",
                column: "PersonId",
                principalTable: "Persons",
                principalColumn: "PersonId",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_StaffVacancy_Vacancies_VacancyId",
                table: "StaffVacancy",
                column: "VacancyId",
                principalTable: "Vacancies",
                principalColumn: "VacancyId",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_DepartmentAccessMatrix_StaffVacancy_StaffId",
                table: "DepartmentAccessMatrix",
                column: "StaffId",
                principalTable: "StaffVacancy",
                principalColumn: "StaffId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_StaffAccessGroups_StaffVacancy_StaffId",
                table: "StaffAccessGroups",
                column: "StaffId",
                principalTable: "StaffVacancy",
                principalColumn: "StaffId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_UserPermissionOverrides_StaffVacancy_StaffId",
                table: "UserPermissionOverrides",
                column: "StaffId",
                principalTable: "StaffVacancy",
                principalColumn: "StaffId",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(name: "FK_DepartmentAccessMatrix_StaffVacancy_StaffId", table: "DepartmentAccessMatrix");
            migrationBuilder.DropForeignKey(name: "FK_StaffAccessGroups_StaffVacancy_StaffId", table: "StaffAccessGroups");
            migrationBuilder.DropForeignKey(name: "FK_UserPermissionOverrides_StaffVacancy_StaffId", table: "UserPermissionOverrides");
            migrationBuilder.DropForeignKey(name: "FK_StaffVacancy_Persons_PersonId", table: "StaffVacancy");
            migrationBuilder.DropForeignKey(name: "FK_StaffVacancy_Vacancies_VacancyId", table: "StaffVacancy");

            migrationBuilder.DropIndex(name: "IX_StaffVacancy_LoginId", table: "StaffVacancy");
            migrationBuilder.DropIndex(name: "IX_StaffVacancy_PersonId", table: "StaffVacancy");
            migrationBuilder.DropIndex(name: "IX_StaffVacancy_VacancyId", table: "StaffVacancy");

            migrationBuilder.DropColumn(name: "LoginId", table: "StaffVacancy");

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "StaffVacancy",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FullName",
                table: "StaffVacancy",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "JoiningDate",
                table: "StaffVacancy",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "GETDATE()");

            migrationBuilder.AddColumn<string>(
                name: "Phone",
                table: "StaffVacancy",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PhotoUrl",
                table: "StaffVacancy",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.RenameTable(name: "StaffVacancy", newName: "Staff");

            migrationBuilder.DropColumn(name: "PersonalEmail", table: "Persons");

            migrationBuilder.AddColumn<int>(
                name: "BranchId",
                table: "Persons",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LoginId",
                table: "Persons",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(name: "IX_Persons_LoginId", table: "Persons", column: "LoginId", unique: true);
        }
    }
}
