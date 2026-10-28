using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Accounts.Migrations.Scheduler;

// Quartz persists assembly-qualified job types. Preserve saved triggers when moving
// the dispatcher from the former module assembly into the Accounts API assembly.
[DbContext(typeof(SchedulerDbContext))]
[Migration("20261027073000_RelocateSchedulerJobTypes")]
public sealed class RelocateSchedulerJobTypes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        UPDATE dbo.QRTZ_JOB_DETAILS
        SET JOB_CLASS_NAME=N'Accounts.Services.Scheduler.ModuleJobDispatcher, Accounts'
        WHERE JOB_CLASS_NAME=N'Accounts.Scheduler.ModuleJobDispatcher, Accounts.Scheduler'
           OR JOB_CLASS_NAME LIKE N'Accounts.Scheduler.ModuleJobDispatcher, Accounts.Scheduler,%';
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        UPDATE dbo.QRTZ_JOB_DETAILS
        SET JOB_CLASS_NAME=N'Accounts.Scheduler.ModuleJobDispatcher, Accounts.Scheduler'
        WHERE JOB_CLASS_NAME=N'Accounts.Services.Scheduler.ModuleJobDispatcher, Accounts'
           OR JOB_CLASS_NAME LIKE N'Accounts.Services.Scheduler.ModuleJobDispatcher, Accounts,%';
        """);
}
