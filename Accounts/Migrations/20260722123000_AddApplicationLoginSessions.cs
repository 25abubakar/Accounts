using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    [Migration("20260722123000_AddApplicationLoginSessions")]
    public partial class AddApplicationLoginSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApplicationLoginSessions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    StaffId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IdentityUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    SessionDate = table.Column<DateOnly>(type: "date", nullable: false),
                    LoginUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LogoutUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    WorkingMinutes = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    IpAddress = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Source = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false, defaultValue: "Software"),
                    Remarks = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationLoginSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApplicationLoginSessions_AspNetUsers_IdentityUserId",
                        column: x => x.IdentityUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ApplicationLoginSessions_Persons_PersonId",
                        column: x => x.PersonId,
                        principalTable: "Persons",
                        principalColumn: "PersonId",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ApplicationLoginSessions_StaffVacancy_StaffId",
                        column: x => x.StaffId,
                        principalTable: "StaffVacancy",
                        principalColumn: "StaffId",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationLoginSessions_IdentityUserId_LogoutUtc",
                table: "ApplicationLoginSessions",
                columns: new[] { "IdentityUserId", "LogoutUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationLoginSessions_PersonId",
                table: "ApplicationLoginSessions",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationLoginSessions_StaffId_SessionDate",
                table: "ApplicationLoginSessions",
                columns: new[] { "StaffId", "SessionDate" });

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationLoginSessions_TenantId_SessionDate",
                table: "ApplicationLoginSessions",
                columns: new[] { "TenantId", "SessionDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApplicationLoginSessions");
        }
    }
}
