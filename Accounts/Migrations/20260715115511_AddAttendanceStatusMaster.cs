using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendanceStatusMaster : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AttendanceStatusMaster",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    StatusName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ColorCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    IsPaid = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceStatusMaster", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "AttendanceStatusMaster",
                columns: new[] { "Id", "Code", "ColorCode", "CreatedDate", "Description", "DisplayOrder", "IsActive", "IsPaid", "ModifiedDate", "StatusName" },
                values: new object[,]
                {
                    { 1, "P", "#10B981", new DateTime(2026, 7, 15, 0, 0, 0, 0, DateTimeKind.Utc), "Employee was present.", 1, true, true, null, "Present" },
                    { 2, "A", "#EF4444", new DateTime(2026, 7, 15, 0, 0, 0, 0, DateTimeKind.Utc), "Employee was absent.", 2, true, false, null, "Absent" },
                    { 3, "L", "#8B5CF6", new DateTime(2026, 7, 15, 0, 0, 0, 0, DateTimeKind.Utc), "Employee was on approved leave.", 3, true, true, null, "Leave" },
                    { 4, "HD", "#F59E0B", new DateTime(2026, 7, 15, 0, 0, 0, 0, DateTimeKind.Utc), "Employee completed a half working day.", 4, true, true, null, "Half Day" },
                    { 5, "LT", "#F97316", new DateTime(2026, 7, 15, 0, 0, 0, 0, DateTimeKind.Utc), "Employee arrived after the scheduled start time.", 5, true, true, null, "Late" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceStatusMaster_Code",
                table: "AttendanceStatusMaster",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceStatusMaster_StatusName",
                table: "AttendanceStatusMaster",
                column: "StatusName",
                unique: true);

            migrationBuilder.Sql(
                """
                DECLARE @ParentId int = (
                    SELECT TOP (1) [Id] FROM [Menus]
                    WHERE [Title] IN (N'Platform Settings', N'Settings') AND [ParentId] IS NULL
                    ORDER BY CASE WHEN [Title] = N'Platform Settings' THEN 0 ELSE 1 END, [Id]
                );

                IF NOT EXISTS (SELECT 1 FROM [Menus] WHERE [Route] = N'/settings/attendance-status')
                BEGIN
                    INSERT INTO [Menus] ([Title], [Icon], [Route], [ParentId], [SortOrder], [IsActive])
                    VALUES (N'Attendance Status', N'CalendarCheck2', N'/settings/attendance-status', @ParentId, 50, 1);
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DECLARE @MenuId int = (SELECT TOP (1) [Id] FROM [Menus] WHERE [Route] = N'/settings/attendance-status');
                IF @MenuId IS NOT NULL
                BEGIN
                    DELETE FROM [MenuPermissions] WHERE [MenuId] = @MenuId;
                    DELETE FROM [StaffMenuAccess] WHERE [MenuId] = @MenuId;
                    DELETE FROM [TenantMenuPermissions] WHERE [MenuId] = @MenuId;
                    DELETE FROM [Menus] WHERE [Id] = @MenuId;
                END
                """);

            migrationBuilder.DropTable(
                name: "AttendanceStatusMaster");
        }
    }
}
