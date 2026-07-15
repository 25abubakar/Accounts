using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendanceRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AttendanceRecords",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttendanceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    AttendanceStatusId = table.Column<int>(type: "int", nullable: true),
                    CheckInUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CheckOutUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    BreakStartedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TotalBreakMinutes = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttendanceRecords_AttendanceStatusMaster_AttendanceStatusId",
                        column: x => x.AttendanceStatusId,
                        principalTable: "AttendanceStatusMaster",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttendanceRecords_Persons_PersonId",
                        column: x => x.PersonId,
                        principalTable: "Persons",
                        principalColumn: "PersonId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRecords_AttendanceStatusId",
                table: "AttendanceRecords",
                column: "AttendanceStatusId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRecords_PersonId_AttendanceDate",
                table: "AttendanceRecords",
                columns: new[] { "PersonId", "AttendanceDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRecords_TenantId_AttendanceDate",
                table: "AttendanceRecords",
                columns: new[] { "TenantId", "AttendanceDate" });

            migrationBuilder.Sql(
                """
                DECLARE @AttendanceParentId int = (
                    SELECT TOP (1) [Id] FROM [Menus]
                    WHERE [Title] = N'Attendance Portal' AND [ParentId] IS NULL
                    ORDER BY [Id]
                );

                IF @AttendanceParentId IS NOT NULL
                   AND NOT EXISTS (SELECT 1 FROM [Menus] WHERE [Route] = N'/attendance')
                BEGIN
                    INSERT INTO [Menus] ([Title], [Icon], [Route], [ParentId], [SortOrder], [IsActive])
                    VALUES (N'Attendance', N'Clock3', N'/attendance', @AttendanceParentId, 1, 1);
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DECLARE @AttendanceMenuId int = (SELECT TOP (1) [Id] FROM [Menus] WHERE [Route] = N'/attendance');
                IF @AttendanceMenuId IS NOT NULL
                BEGIN
                    DELETE FROM [MenuPermissions] WHERE [MenuId] = @AttendanceMenuId;
                    DELETE FROM [StaffMenuAccess] WHERE [MenuId] = @AttendanceMenuId;
                    DELETE FROM [TenantMenuPermissions] WHERE [MenuId] = @AttendanceMenuId;
                    DELETE FROM [Menus] WHERE [Id] = @AttendanceMenuId;
                END
                """);

            migrationBuilder.DropTable(
                name: "AttendanceRecords");
        }
    }
}
