using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations.Scheduler
{
    /// <inheritdoc />
    public partial class InitialStandaloneScheduler : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            using var stream=typeof(InitialStandaloneScheduler).Assembly.GetManifestResourceStream("Accounts.Scheduler.Migrations.Sql.Quartz.SqlServer.3.22.0.sql")
                ?? throw new InvalidOperationException("Quartz schema resource is missing.");
            using var reader=new System.IO.StreamReader(stream);
            foreach(var batch in System.Text.RegularExpressions.Regex.Split(reader.ReadToEnd(),@"^\s*GO\s*$",System.Text.RegularExpressions.RegexOptions.Multiline|System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                if(!string.IsNullOrWhiteSpace(batch))migrationBuilder.Sql(batch);
            migrationBuilder.CreateTable(
                name: "SchedulerDefinitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Handler = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Mode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    IntervalDays = table.Column<int>(type: "int", nullable: false),
                    DayOfMonth = table.Column<int>(type: "int", nullable: false),
                    DayOfWeek = table.Column<int>(type: "int", nullable: false),
                    Time = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    StartLocal = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TimeZone = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    MisfirePolicy = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    TimeoutSeconds = table.Column<int>(type: "int", nullable: false),
                    RetryCount = table.Column<int>(type: "int", nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreationKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SchedulerDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SchedulerCommands",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    ScheduleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SchedulerCommands", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SchedulerCommands_SchedulerDefinitions_ScheduleId",
                        column: x => x.ScheduleId,
                        principalTable: "SchedulerDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SchedulerRuns",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ScheduleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    Occurrence = table.Column<string>(type: "nvarchar(140)", maxLength: 140, nullable: false),
                    ScheduledUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StartedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FinishedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Attempt = table.Column<int>(type: "int", nullable: false),
                    Result = table.Column<string>(type: "nvarchar(2500)", maxLength: 2500, nullable: true),
                    Error = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SchedulerRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SchedulerRuns_SchedulerDefinitions_ScheduleId",
                        column: x => x.ScheduleId,
                        principalTable: "SchedulerDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SchedulerCommands_ScheduleId",
                table: "SchedulerCommands",
                column: "ScheduleId");

            migrationBuilder.CreateIndex(
                name: "IX_SchedulerDefinitions_Enabled_UpdatedUtc",
                table: "SchedulerDefinitions",
                columns: new[] { "Enabled", "UpdatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SchedulerDefinitions_TenantId_CreationKey",
                table: "SchedulerDefinitions",
                columns: new[] { "TenantId", "CreationKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SchedulerRuns_ScheduleId",
                table: "SchedulerRuns",
                column: "ScheduleId");

            migrationBuilder.CreateIndex(
                name: "IX_SchedulerRuns_TenantId_ScheduleId_Occurrence",
                table: "SchedulerRuns",
                columns: new[] { "TenantId", "ScheduleId", "Occurrence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SchedulerRuns_TenantId_StartedUtc",
                table: "SchedulerRuns",
                columns: new[] { "TenantId", "StartedUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SchedulerCommands");

            migrationBuilder.DropTable(
                name: "SchedulerRuns");

            migrationBuilder.DropTable(
                name: "SchedulerDefinitions");
        }
    }
}
