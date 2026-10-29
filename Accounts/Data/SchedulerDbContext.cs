using Accounts.Models.Scheduler;
using Accounts.Services.Scheduler;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Data;

public sealed class SchedulerDbContext(DbContextOptions<SchedulerDbContext> options,SchedulerTenant tenant) : DbContext(options)
{
    public DbSet<SchedulerTaskRecord> Tasks=>Set<SchedulerTaskRecord>();
    public DbSet<ScheduleDefinition> Definitions=>Set<ScheduleDefinition>();
    public DbSet<ScheduleRun> Runs=>Set<ScheduleRun>();
    public DbSet<SchedulerCommand> Commands=>Set<SchedulerCommand>();
    public DbSet<SchedulerRealtimeEvent> RealtimeEvents=>Set<SchedulerRealtimeEvent>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasSequence<long>("SchedulerRunNumber");
        model.Entity<SchedulerTaskRecord>(e=>{
            e.ToTable("SchedulerTasks");e.HasKey(x=>x.RecordId);e.Property(x=>x.RecordId).UseIdentityColumn();
            e.HasQueryFilter(x=>x.TenantId==tenant.Id);
            e.HasAlternateKey(x=>new{x.RecordId,x.TenantId});
            e.HasOne<SchedulerTaskRecord>().WithMany().HasForeignKey(x=>new{x.ParentRecordId,x.TenantId})
                .HasPrincipalKey(x=>new{x.RecordId,x.TenantId}).OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_SchedulerTasks_Parent");
            e.HasIndex(x=>new{x.RecordKind,x.EntityId}).IsUnique();
            e.HasIndex(x=>x.RunNumber).IsUnique().HasFilter("[RecordKind]='RUN'");
            e.HasIndex(x=>new{x.TenantId,x.ManagedKey}).IsUnique().HasFilter("[RecordKind]='JOB' AND [ManagedKey] IS NOT NULL");
            e.HasIndex(x=>new{x.TenantId,x.CreationKey}).IsUnique().HasFilter("[RecordKind]='JOB'");
            e.HasIndex(x=>new{x.ParentRecordId,x.Occurrence}).IsUnique().HasFilter("[RecordKind]='RUN'");
            e.HasIndex(x=>x.Handler).IsUnique().HasFilter("[RecordKind]='CATALOG'");
            e.HasIndex(x=>x.Name).IsUnique().HasFilter("[RecordKind]='SETTING'");
            e.HasIndex(x=>new{x.RecordKind,x.Enabled,x.NextRunUtc});
            e.HasIndex(x=>new{x.RecordKind,x.Status,x.NextAttemptUtc,x.LeaseUntilUtc});
            e.HasIndex(x=>new{x.RecordKind,x.DispatchedUtc,x.CreatedUtc});
            e.HasIndex(x=>new{x.RecordKind,x.DeliveredUtc,x.CreatedUtc});
            e.HasIndex(x=>new{x.TenantId,x.RecordKind,x.ParentRecordId,x.RecordId});
        });
        model.Entity<ScheduleDefinition>(e=>{e.ToView("vw_SchedulerDefinitions");e.HasKey(x=>x.Id);e.HasQueryFilter(x=>x.TenantId==tenant.Id);});
        model.Entity<ScheduleRun>(e=>{e.ToView("vw_SchedulerRuns");e.HasKey(x=>x.Id);e.HasQueryFilter(x=>x.TenantId==tenant.Id);});
        model.Entity<SchedulerCommand>(e=>{e.ToView("vw_SchedulerCommands");e.HasKey(x=>x.Id);e.HasQueryFilter(x=>x.TenantId==tenant.Id);});
        model.Entity<SchedulerRealtimeEvent>(e=>{e.ToView("vw_SchedulerRealtimeEvents");e.HasKey(x=>x.Id);e.HasQueryFilter(x=>x.TenantId==tenant.Id);});
    }
    public override int SaveChanges(bool acceptAllChangesOnSuccess)=>throw ReadOnlyContext();
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess,CancellationToken cancellationToken=default)=>throw ReadOnlyContext();
    private static InvalidOperationException ReadOnlyContext()=>new("Scheduler data is read through views and changed only by its stored procedures.");
}
