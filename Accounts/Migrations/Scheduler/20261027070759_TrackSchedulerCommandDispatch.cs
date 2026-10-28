using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations.Scheduler
{
    /// <inheritdoc />
    public partial class TrackSchedulerCommandDispatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DispatchedUtc",
                table: "SchedulerCommands",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SchedulerCommands_DispatchedUtc",
                table: "SchedulerCommands",
                column: "DispatchedUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SchedulerCommands_DispatchedUtc",
                table: "SchedulerCommands");

            migrationBuilder.DropColumn(
                name: "DispatchedUtc",
                table: "SchedulerCommands");
        }
    }
}
