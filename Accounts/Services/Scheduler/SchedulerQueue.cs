using Accounts.Data;
using Accounts.Models.Scheduler;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Scheduler;

public sealed class SchedulerQueue(IServiceScopeFactory scopes, SchedulerStorage storage, ScheduleSynchronizer synchronizer)
{
    public async Task<IReadOnlyList<long>> CollectAsync(CancellationToken ct)
    {
        await using var lease = await SchedulerSqlLease.TryAcquireAsync(storage.SchedulerConnection, "AccountsScheduler:Queue", ct);
        if (lease == null) return [];
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SchedulerDbContext>();
        var changed = await db.Definitions.IgnoreQueryFilters().Where(x => x.RuntimeRevision != x.Revision)
            .Select(x => x.Id).Take(100).ToListAsync(ct);
        foreach (var id in changed) await synchronizer.ApplyAsync(id, ct);

        var now = DateTime.UtcNow;
        var commands = await db.Commands.IgnoreQueryFilters().Where(x => x.DispatchedUtc == null)
            .OrderBy(x => x.CreatedUtc).Take(100).ToListAsync(ct);
        foreach (var command in commands)
        {
            var occurrence = "manual-" + command.Id.ToString("N");
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            if (!await db.Runs.IgnoreQueryFilters().AnyAsync(x => x.TenantId == command.TenantId && x.ScheduleId == command.ScheduleId && x.Occurrence == occurrence, ct))
            {
                var definition = await db.Definitions.IgnoreQueryFilters().AsNoTracking()
                    .SingleAsync(x => x.Id == command.ScheduleId && x.TenantId == command.TenantId, ct);
                db.Runs.Add(new ScheduleRun { TenantId = command.TenantId, ScheduleId = command.ScheduleId,
                    Occurrence = occurrence, ScheduledUtc = command.CreatedUtc, Status = "Queued", IsManual = true,
                    ScheduleRevision = definition.Revision, NextAttemptUtc = now });
            }
            command.DispatchedUtc = now;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }

        var due = await db.Definitions.IgnoreQueryFilters().Where(x => x.Enabled && x.NextRunUtc <= now && x.RuntimeRevision == x.Revision)
            .OrderBy(x => x.NextRunUtc).Take(100).ToListAsync(ct);
        foreach (var definition in due)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var scheduled = definition.NextRunUtc!.Value;
            // A small dispatch tolerance avoids treating normal poll latency as a missed run.
            if (definition.MisfirePolicy == "RunOnce" || scheduled >= now.AddSeconds(-60))
            {
                var occurrence = "scheduled-" + scheduled.Ticks;
                if (!await db.Runs.IgnoreQueryFilters().AnyAsync(x => x.TenantId == definition.TenantId && x.ScheduleId == definition.Id && x.Occurrence == occurrence, ct))
                    db.Runs.Add(new ScheduleRun { TenantId = definition.TenantId, ScheduleId = definition.Id,
                        Occurrence = occurrence, ScheduledUtc = scheduled, ScheduleRevision = definition.Revision,
                        Status = "Queued", NextAttemptUtc = now });
            }
            definition.NextRunUtc = ScheduleTiming.NextAfter(definition, now);
            try { await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); }
            catch (DbUpdateConcurrencyException) { await transaction.RollbackAsync(ct); db.ChangeTracker.Clear(); }
        }

        return await db.Runs.IgnoreQueryFilters().AsNoTracking()
            .Where(x => ((x.Status == "Queued" || x.Status == "RetryScheduled") && x.NextAttemptUtc <= now)
                || (x.Status == "Running" && x.LeaseUntilUtc <= now))
            .OrderBy(x => x.ScheduledUtc).Select(x => x.Id).Take(100).ToListAsync(ct);
    }
}
