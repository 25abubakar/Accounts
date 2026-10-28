using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations.Scheduler
{
    /// <inheritdoc />
    public partial class AddWindowsSchedulerRuntime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsManual",
                table: "SchedulerRuns",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LeaseUntilUtc",
                table: "SchedulerRuns",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextAttemptUtc",
                table: "SchedulerRuns",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ScheduleRevision",
                table: "SchedulerRuns",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextRunUtc",
                table: "SchedulerDefinitions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RuntimeRevision",
                table: "SchedulerDefinitions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_SchedulerRuns_Status_NextAttemptUtc",
                table: "SchedulerRuns",
                columns: new[] { "Status", "NextAttemptUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SchedulerDefinitions_Enabled_NextRunUtc",
                table: "SchedulerDefinitions",
                columns: new[] { "Enabled", "NextRunUtc" });

            // Keep existing pending fire times instead of replaying every schedule from its original anchor.
            // Quartz versions have used .NET ticks and Unix milliseconds; recognize both encodings.
            migrationBuilder.Sql("""
                UPDATE definition SET
                    RuntimeRevision=CASE WHEN definition.Enabled=0 OR job.JOB_NAME IS NOT NULL THEN definition.Revision ELSE 0 END,
                    NextRunUtc=CASE WHEN definition.Enabled=1 AND triggerRow.NEXT_FIRE_TIME>=0 THEN
                        CASE WHEN triggerRow.NEXT_FIRE_TIME>100000000000000000 THEN
                            DATEADD(second,CONVERT(int,(triggerRow.NEXT_FIRE_TIME%864000000000)/10000000),
                                DATEADD(day,CONVERT(int,triggerRow.NEXT_FIRE_TIME/864000000000),CONVERT(datetime2,'00010101')))
                        ELSE DATEADD(millisecond,CONVERT(int,triggerRow.NEXT_FIRE_TIME%86400000),
                                DATEADD(day,CONVERT(int,triggerRow.NEXT_FIRE_TIME/86400000),CONVERT(datetime2,'19700101'))) END
                        ELSE NULL END
                FROM dbo.SchedulerDefinitions definition
                LEFT JOIN dbo.QRTZ_JOB_DETAILS job ON job.SCHED_NAME=N'AccountsScheduler'
                    AND job.JOB_NAME=REPLACE(CONVERT(nvarchar(36),definition.Id),N'-',N'')
                    AND job.JOB_GROUP=CONCAT(N'tenant-',definition.TenantId)
                LEFT JOIN dbo.QRTZ_TRIGGERS triggerRow ON triggerRow.SCHED_NAME=job.SCHED_NAME
                    AND triggerRow.JOB_NAME=job.JOB_NAME AND triggerRow.JOB_GROUP=job.JOB_GROUP
                    AND triggerRow.TRIGGER_NAME=CONCAT(N'schedule-',REPLACE(CONVERT(nvarchar(36),definition.Id),N'-',N''));

                UPDATE run SET IsManual=CASE WHEN run.Occurrence LIKE N'manual-%' THEN 1 ELSE 0 END,
                    ScheduleRevision=definition.Revision,
                    NextAttemptUtc=CASE WHEN run.Status IN(N'RetryScheduled',N'Queued') THEN SYSUTCDATETIME() ELSE NULL END,
                    LeaseUntilUtc=CASE WHEN run.Status=N'Running' THEN DATEADD(second,definition.TimeoutSeconds+30,run.StartedUtc) ELSE NULL END
                FROM dbo.SchedulerRuns run JOIN dbo.SchedulerDefinitions definition
                    ON definition.Id=run.ScheduleId AND definition.TenantId=run.TenantId;

                UPDATE command SET DispatchedUtc=NULL FROM dbo.SchedulerCommands command
                WHERE NOT EXISTS(SELECT 1 FROM dbo.SchedulerRuns run WHERE run.ScheduleId=command.ScheduleId
                    AND run.TenantId=command.TenantId AND run.Occurrence=CONCAT(N'manual-',REPLACE(CONVERT(nvarchar(36),command.Id),N'-',N'')));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SchedulerRuns_Status_NextAttemptUtc",
                table: "SchedulerRuns");

            migrationBuilder.DropIndex(
                name: "IX_SchedulerDefinitions_Enabled_NextRunUtc",
                table: "SchedulerDefinitions");

            migrationBuilder.DropColumn(
                name: "IsManual",
                table: "SchedulerRuns");

            migrationBuilder.DropColumn(
                name: "LeaseUntilUtc",
                table: "SchedulerRuns");

            migrationBuilder.DropColumn(
                name: "NextAttemptUtc",
                table: "SchedulerRuns");

            migrationBuilder.DropColumn(
                name: "ScheduleRevision",
                table: "SchedulerRuns");

            migrationBuilder.DropColumn(
                name: "NextRunUtc",
                table: "SchedulerDefinitions");

            migrationBuilder.DropColumn(
                name: "RuntimeRevision",
                table: "SchedulerDefinitions");
        }
    }
}
