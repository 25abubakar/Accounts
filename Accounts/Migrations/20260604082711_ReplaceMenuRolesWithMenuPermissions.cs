using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceMenuRolesWithMenuPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AccessGroupFeatures_Features_FeatureKey",
                table: "AccessGroupFeatures");

            migrationBuilder.DropForeignKey(
                name: "FK_DepartmentAccessMatrix_Features_FeatureKey",
                table: "DepartmentAccessMatrix");

            migrationBuilder.DropForeignKey(
                name: "FK_RolePermissions_Features_FeatureKey",
                table: "RolePermissions");

            migrationBuilder.DropForeignKey(
                name: "FK_UserPermissionOverrides_Features_FeatureKey",
                table: "UserPermissionOverrides");

            migrationBuilder.DropTable(
                name: "MenuRoles");

            // ── Clear all FK-dependent rows before schema change ─────────────
            // These tables used string FeatureKey as FK. After this migration
            // they use integer PermissionId. Old rows cannot be migrated
            // automatically — reseed via POST /api/rbac/seed-features then
            // re-assign permissions through the admin UI.
            migrationBuilder.Sql("DELETE FROM UserPermissionOverrides");
            migrationBuilder.Sql("DELETE FROM RolePermissions");
            migrationBuilder.Sql("DELETE FROM DepartmentAccessMatrix");
            migrationBuilder.Sql("DELETE FROM AccessGroupFeatures");

            migrationBuilder.DropIndex(
                name: "IX_UserPermissionOverrides_FeatureKey",
                table: "UserPermissionOverrides");

            migrationBuilder.DropIndex(
                name: "IX_UserPermissionOverrides_StaffId_FeatureKey",
                table: "UserPermissionOverrides");

            migrationBuilder.DropIndex(
                name: "IX_RolePermissions_FeatureKey",
                table: "RolePermissions");

            migrationBuilder.DropIndex(
                name: "IX_RolePermissions_JobTitle_DeptId_FeatureKey",
                table: "RolePermissions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Features",
                table: "Features");

            migrationBuilder.DropIndex(
                name: "IX_DepartmentAccessMatrix_FeatureKey",
                table: "DepartmentAccessMatrix");

            migrationBuilder.DropIndex(
                name: "IX_DepartmentAccessMatrix_StaffId_FeatureKey",
                table: "DepartmentAccessMatrix");

            migrationBuilder.DropPrimaryKey(
                name: "PK_AccessGroupFeatures",
                table: "AccessGroupFeatures");

            migrationBuilder.DropIndex(
                name: "IX_AccessGroupFeatures_FeatureKey",
                table: "AccessGroupFeatures");

            migrationBuilder.DropColumn(
                name: "FeatureKey",
                table: "UserPermissionOverrides");

            migrationBuilder.DropColumn(
                name: "FeatureKey",
                table: "RolePermissions");

            migrationBuilder.DropColumn(
                name: "FeatureKey",
                table: "DepartmentAccessMatrix");

            migrationBuilder.DropColumn(
                name: "FeatureKey",
                table: "AccessGroupFeatures");

            migrationBuilder.AddColumn<int>(
                name: "PermissionId",
                table: "UserPermissionOverrides",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PermissionId",
                table: "RolePermissions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PermissionId",
                table: "Features",
                type: "int",
                nullable: false,
                defaultValue: 0)
                .Annotation("SqlServer:Identity", "1, 1");

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedDate",
                table: "Features",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "GETDATE()");

            migrationBuilder.AddColumn<int>(
                name: "PermissionId",
                table: "DepartmentAccessMatrix",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PermissionId",
                table: "AccessGroupFeatures",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddPrimaryKey(
                name: "PK_Features",
                table: "Features",
                column: "PermissionId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_AccessGroupFeatures",
                table: "AccessGroupFeatures",
                columns: new[] { "GroupId", "PermissionId" });

            migrationBuilder.CreateTable(
                name: "EmployeeByOrgAndRoleDto",
                columns: table => new
                {
                    OrganizationId = table.Column<int>(type: "int", nullable: false),
                    OrganizationName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    VacancyCode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    JobTitle = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                });

            migrationBuilder.CreateTable(
                name: "MenuPermissions",
                columns: table => new
                {
                    MenuId = table.Column<int>(type: "int", nullable: false),
                    PermissionId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MenuPermissions", x => new { x.MenuId, x.PermissionId });
                    table.ForeignKey(
                        name: "FK_MenuPermissions_Features_PermissionId",
                        column: x => x.PermissionId,
                        principalTable: "Features",
                        principalColumn: "PermissionId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MenuPermissions_Menus_MenuId",
                        column: x => x.MenuId,
                        principalTable: "Menus",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationVacancyPersonDto",
                columns: table => new
                {
                    OrganizationId = table.Column<int>(type: "int", nullable: false),
                    OrganizationName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OrganizationCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    VacancyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VacancyCode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    JobTitle = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Department = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsFilled = table.Column<bool>(type: "bit", nullable: false),
                    PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FullName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Email = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserPermissionOverrides_PermissionId",
                table: "UserPermissionOverrides",
                column: "PermissionId");

            migrationBuilder.CreateIndex(
                name: "IX_UserPermissionOverrides_StaffId",
                table: "UserPermissionOverrides",
                column: "StaffId");

            migrationBuilder.CreateIndex(
                name: "IX_UserPermissionOverrides_StaffId_PermissionId",
                table: "UserPermissionOverrides",
                columns: new[] { "StaffId", "PermissionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserPermissionOverrides_StaffId_Status",
                table: "UserPermissionOverrides",
                columns: new[] { "StaffId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_RolePermissions_JobTitle",
                table: "RolePermissions",
                column: "JobTitle");

            migrationBuilder.CreateIndex(
                name: "IX_RolePermissions_JobTitle_DeptId",
                table: "RolePermissions",
                columns: new[] { "JobTitle", "DeptId" });

            migrationBuilder.CreateIndex(
                name: "IX_RolePermissions_JobTitle_DeptId_PermissionId",
                table: "RolePermissions",
                columns: new[] { "JobTitle", "DeptId", "PermissionId" },
                unique: true,
                filter: "[DeptId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RolePermissions_PermissionId",
                table: "RolePermissions",
                column: "PermissionId");

            migrationBuilder.CreateIndex(
                name: "IX_Features_FeatureKey",
                table: "Features",
                column: "FeatureKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DepartmentAccessMatrix_PermissionId",
                table: "DepartmentAccessMatrix",
                column: "PermissionId");

            migrationBuilder.CreateIndex(
                name: "IX_DepartmentAccessMatrix_StaffId",
                table: "DepartmentAccessMatrix",
                column: "StaffId");

            migrationBuilder.CreateIndex(
                name: "IX_DepartmentAccessMatrix_StaffId_PermissionId",
                table: "DepartmentAccessMatrix",
                columns: new[] { "StaffId", "PermissionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccessGroupFeatures_GroupId",
                table: "AccessGroupFeatures",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_AccessGroupFeatures_PermissionId",
                table: "AccessGroupFeatures",
                column: "PermissionId");

            migrationBuilder.CreateIndex(
                name: "IX_MenuPermissions_MenuId",
                table: "MenuPermissions",
                column: "MenuId");

            migrationBuilder.CreateIndex(
                name: "IX_MenuPermissions_PermissionId",
                table: "MenuPermissions",
                column: "PermissionId");

            migrationBuilder.AddForeignKey(
                name: "FK_AccessGroupFeatures_Features_PermissionId",
                table: "AccessGroupFeatures",
                column: "PermissionId",
                principalTable: "Features",
                principalColumn: "PermissionId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_DepartmentAccessMatrix_Features_PermissionId",
                table: "DepartmentAccessMatrix",
                column: "PermissionId",
                principalTable: "Features",
                principalColumn: "PermissionId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_RolePermissions_Features_PermissionId",
                table: "RolePermissions",
                column: "PermissionId",
                principalTable: "Features",
                principalColumn: "PermissionId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_UserPermissionOverrides_Features_PermissionId",
                table: "UserPermissionOverrides",
                column: "PermissionId",
                principalTable: "Features",
                principalColumn: "PermissionId",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AccessGroupFeatures_Features_PermissionId",
                table: "AccessGroupFeatures");

            migrationBuilder.DropForeignKey(
                name: "FK_DepartmentAccessMatrix_Features_PermissionId",
                table: "DepartmentAccessMatrix");

            migrationBuilder.DropForeignKey(
                name: "FK_RolePermissions_Features_PermissionId",
                table: "RolePermissions");

            migrationBuilder.DropForeignKey(
                name: "FK_UserPermissionOverrides_Features_PermissionId",
                table: "UserPermissionOverrides");

            migrationBuilder.DropTable(
                name: "EmployeeByOrgAndRoleDto");

            migrationBuilder.DropTable(
                name: "MenuPermissions");

            migrationBuilder.DropTable(
                name: "OrganizationVacancyPersonDto");

            migrationBuilder.DropIndex(
                name: "IX_UserPermissionOverrides_PermissionId",
                table: "UserPermissionOverrides");

            migrationBuilder.DropIndex(
                name: "IX_UserPermissionOverrides_StaffId",
                table: "UserPermissionOverrides");

            migrationBuilder.DropIndex(
                name: "IX_UserPermissionOverrides_StaffId_PermissionId",
                table: "UserPermissionOverrides");

            migrationBuilder.DropIndex(
                name: "IX_UserPermissionOverrides_StaffId_Status",
                table: "UserPermissionOverrides");

            migrationBuilder.DropIndex(
                name: "IX_RolePermissions_JobTitle",
                table: "RolePermissions");

            migrationBuilder.DropIndex(
                name: "IX_RolePermissions_JobTitle_DeptId",
                table: "RolePermissions");

            migrationBuilder.DropIndex(
                name: "IX_RolePermissions_JobTitle_DeptId_PermissionId",
                table: "RolePermissions");

            migrationBuilder.DropIndex(
                name: "IX_RolePermissions_PermissionId",
                table: "RolePermissions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Features",
                table: "Features");

            migrationBuilder.DropIndex(
                name: "IX_Features_FeatureKey",
                table: "Features");

            migrationBuilder.DropIndex(
                name: "IX_DepartmentAccessMatrix_PermissionId",
                table: "DepartmentAccessMatrix");

            migrationBuilder.DropIndex(
                name: "IX_DepartmentAccessMatrix_StaffId",
                table: "DepartmentAccessMatrix");

            migrationBuilder.DropIndex(
                name: "IX_DepartmentAccessMatrix_StaffId_PermissionId",
                table: "DepartmentAccessMatrix");

            migrationBuilder.DropPrimaryKey(
                name: "PK_AccessGroupFeatures",
                table: "AccessGroupFeatures");

            migrationBuilder.DropIndex(
                name: "IX_AccessGroupFeatures_GroupId",
                table: "AccessGroupFeatures");

            migrationBuilder.DropIndex(
                name: "IX_AccessGroupFeatures_PermissionId",
                table: "AccessGroupFeatures");

            migrationBuilder.DropColumn(
                name: "PermissionId",
                table: "UserPermissionOverrides");

            migrationBuilder.DropColumn(
                name: "PermissionId",
                table: "RolePermissions");

            migrationBuilder.DropColumn(
                name: "PermissionId",
                table: "Features");

            migrationBuilder.DropColumn(
                name: "CreatedDate",
                table: "Features");

            migrationBuilder.DropColumn(
                name: "PermissionId",
                table: "DepartmentAccessMatrix");

            migrationBuilder.DropColumn(
                name: "PermissionId",
                table: "AccessGroupFeatures");

            migrationBuilder.AddColumn<string>(
                name: "FeatureKey",
                table: "UserPermissionOverrides",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "FeatureKey",
                table: "RolePermissions",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "FeatureKey",
                table: "DepartmentAccessMatrix",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "FeatureKey",
                table: "AccessGroupFeatures",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Features",
                table: "Features",
                column: "FeatureKey");

            migrationBuilder.AddPrimaryKey(
                name: "PK_AccessGroupFeatures",
                table: "AccessGroupFeatures",
                columns: new[] { "GroupId", "FeatureKey" });

            migrationBuilder.CreateTable(
                name: "MenuRoles",
                columns: table => new
                {
                    MenuId = table.Column<int>(type: "int", nullable: false),
                    RoleName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MenuRoles", x => new { x.MenuId, x.RoleName });
                    table.ForeignKey(
                        name: "FK_MenuRoles_Menus_MenuId",
                        column: x => x.MenuId,
                        principalTable: "Menus",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserPermissionOverrides_FeatureKey",
                table: "UserPermissionOverrides",
                column: "FeatureKey");

            migrationBuilder.CreateIndex(
                name: "IX_UserPermissionOverrides_StaffId_FeatureKey",
                table: "UserPermissionOverrides",
                columns: new[] { "StaffId", "FeatureKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RolePermissions_FeatureKey",
                table: "RolePermissions",
                column: "FeatureKey");

            migrationBuilder.CreateIndex(
                name: "IX_RolePermissions_JobTitle_DeptId_FeatureKey",
                table: "RolePermissions",
                columns: new[] { "JobTitle", "DeptId", "FeatureKey" },
                unique: true,
                filter: "[DeptId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DepartmentAccessMatrix_FeatureKey",
                table: "DepartmentAccessMatrix",
                column: "FeatureKey");

            migrationBuilder.CreateIndex(
                name: "IX_DepartmentAccessMatrix_StaffId_FeatureKey",
                table: "DepartmentAccessMatrix",
                columns: new[] { "StaffId", "FeatureKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccessGroupFeatures_FeatureKey",
                table: "AccessGroupFeatures",
                column: "FeatureKey");

            migrationBuilder.AddForeignKey(
                name: "FK_AccessGroupFeatures_Features_FeatureKey",
                table: "AccessGroupFeatures",
                column: "FeatureKey",
                principalTable: "Features",
                principalColumn: "FeatureKey",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_DepartmentAccessMatrix_Features_FeatureKey",
                table: "DepartmentAccessMatrix",
                column: "FeatureKey",
                principalTable: "Features",
                principalColumn: "FeatureKey",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_RolePermissions_Features_FeatureKey",
                table: "RolePermissions",
                column: "FeatureKey",
                principalTable: "Features",
                principalColumn: "FeatureKey",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_UserPermissionOverrides_Features_FeatureKey",
                table: "UserPermissionOverrides",
                column: "FeatureKey",
                principalTable: "Features",
                principalColumn: "FeatureKey",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
