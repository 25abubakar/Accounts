using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class AddPBACTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccessGroups",
                columns: table => new
                {
                    GroupId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GroupName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessGroups", x => x.GroupId);
                });

            migrationBuilder.CreateTable(
                name: "Features",
                columns: table => new
                {
                    FeatureKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FeatureName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Module = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Features", x => x.FeatureKey);
                });

            migrationBuilder.CreateTable(
                name: "StaffAccessGroups",
                columns: table => new
                {
                    StaffId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GroupId = table.Column<int>(type: "int", nullable: false),
                    AssignedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    AssignedDate = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETDATE()"),
                    Note = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffAccessGroups", x => new { x.StaffId, x.GroupId });
                    table.ForeignKey(
                        name: "FK_StaffAccessGroups_AccessGroups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "AccessGroups",
                        principalColumn: "GroupId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StaffAccessGroups_Staff_StaffId",
                        column: x => x.StaffId,
                        principalTable: "Staff",
                        principalColumn: "StaffId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AccessGroupFeatures",
                columns: table => new
                {
                    GroupId = table.Column<int>(type: "int", nullable: false),
                    FeatureKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessGroupFeatures", x => new { x.GroupId, x.FeatureKey });
                    table.ForeignKey(
                        name: "FK_AccessGroupFeatures_AccessGroups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "AccessGroups",
                        principalColumn: "GroupId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AccessGroupFeatures_Features_FeatureKey",
                        column: x => x.FeatureKey,
                        principalTable: "Features",
                        principalColumn: "FeatureKey",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DepartmentAccessMatrix",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StaffId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeptId = table.Column<int>(type: "int", nullable: false),
                    FeatureKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    HasAccess = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    GrantedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    GrantedDate = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DepartmentAccessMatrix", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DepartmentAccessMatrix_Features_FeatureKey",
                        column: x => x.FeatureKey,
                        principalTable: "Features",
                        principalColumn: "FeatureKey",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DepartmentAccessMatrix_OrganizationTree_DeptId",
                        column: x => x.DeptId,
                        principalSchema: "dbo",
                        principalTable: "OrganizationTree",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DepartmentAccessMatrix_Staff_StaffId",
                        column: x => x.StaffId,
                        principalTable: "Staff",
                        principalColumn: "StaffId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccessGroupFeatures_FeatureKey",
                table: "AccessGroupFeatures",
                column: "FeatureKey");

            migrationBuilder.CreateIndex(
                name: "IX_DepartmentAccessMatrix_DeptId",
                table: "DepartmentAccessMatrix",
                column: "DeptId");

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
                name: "IX_StaffAccessGroups_GroupId",
                table: "StaffAccessGroups",
                column: "GroupId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccessGroupFeatures");

            migrationBuilder.DropTable(
                name: "DepartmentAccessMatrix");

            migrationBuilder.DropTable(
                name: "StaffAccessGroups");

            migrationBuilder.DropTable(
                name: "Features");

            migrationBuilder.DropTable(
                name: "AccessGroups");
        }
    }
}
