using Accounts.Models.Scheduler;
using Accounts.Services.Scheduler;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Data;

public sealed class SchedulerDbContext(DbContextOptions<SchedulerDbContext> options,SchedulerTenant tenant) : DbContext(options)
{
    public DbSet<ScheduleDefinition> Definitions => Set<ScheduleDefinition>();
    public DbSet<ScheduleRun> Runs => Set<ScheduleRun>();
    public DbSet<SchedulerCommand> Commands => Set<SchedulerCommand>();
    public DbSet<SchedulerRealtimeEvent> RealtimeEvents => Set<SchedulerRealtimeEvent>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<SchedulerRealtimeEvent>(e=>{
            e.ToTable("SchedulerRealtimeEvents");e.HasKey(x=>x.Id);e.HasQueryFilter(x=>x.TenantId==tenant.Id);
            e.HasIndex(x=>new{x.DeliveredUtc,x.CreatedUtc});
        });
        model.Entity<ScheduleDefinition>(e=>{
            e.ToTable("SchedulerDefinitions");e.HasKey(x=>x.Id);e.HasQueryFilter(x=>x.TenantId==tenant.Id);
            e.HasIndex(x=>new{x.TenantId,x.CreationKey}).IsUnique();e.HasIndex(x=>new{x.Enabled,x.UpdatedUtc});
            e.HasIndex(x=>new{x.Enabled,x.NextRunUtc});
            e.HasIndex(x=>new{x.TenantId,x.ManagedKey}).IsUnique().HasFilter("[ManagedKey] IS NOT NULL");
        });
        model.Entity<ScheduleRun>(e=>{
            e.ToTable("SchedulerRuns");e.HasKey(x=>x.Id);e.HasQueryFilter(x=>x.TenantId==tenant.Id);
            e.HasIndex(x=>new{x.TenantId,x.ScheduleId,x.Occurrence}).IsUnique();
            e.HasIndex(x=>new{x.TenantId,x.StartedUtc});
            e.HasIndex(x=>new{x.Status,x.NextAttemptUtc});
            e.HasOne<ScheduleDefinition>().WithMany().HasForeignKey(x=>x.ScheduleId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<SchedulerCommand>(e=>{
            e.ToTable("SchedulerCommands");e.HasKey(x=>x.Id);e.HasQueryFilter(x=>x.TenantId==tenant.Id);
            e.HasIndex(x=>x.DispatchedUtc);
            e.HasOne<ScheduleDefinition>().WithMany().HasForeignKey(x=>x.ScheduleId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
