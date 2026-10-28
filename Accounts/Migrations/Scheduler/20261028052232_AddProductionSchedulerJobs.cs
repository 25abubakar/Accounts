using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations.Scheduler
{
    /// <inheritdoc />
    public partial class AddProductionSchedulerJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "IntervalMinutes",
                table: "SchedulerDefinitions",
                type: "int",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.AddColumn<string>(
                name: "ManagedKey",
                table: "SchedulerDefinitions",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SchedulerRealtimeEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DeliveredUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SchedulerRealtimeEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SchedulerDefinitions_TenantId_ManagedKey",
                table: "SchedulerDefinitions",
                columns: new[] { "TenantId", "ManagedKey" },
                unique: true,
                filter: "[ManagedKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SchedulerRealtimeEvents_DeliveredUtc_CreatedUtc",
                table: "SchedulerRealtimeEvents",
                columns: new[] { "DeliveredUtc", "CreatedUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SchedulerRealtimeEvents");

            migrationBuilder.DropIndex(
                name: "IX_SchedulerDefinitions_TenantId_ManagedKey",
                table: "SchedulerDefinitions");

            migrationBuilder.DropColumn(
                name: "IntervalMinutes",
                table: "SchedulerDefinitions");

            migrationBuilder.DropColumn(
                name: "ManagedKey",
                table: "SchedulerDefinitions");
        }
    }
}
