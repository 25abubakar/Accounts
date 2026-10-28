using Accounts.Data;
using Accounts.Models.Scheduler;
using Accounts.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Scheduler;

public sealed class ModuleJobDispatcher(SchedulerDbContext db, IEnumerable<IModuleJob> handlers,
    SchedulerStorage storage, ILogger<ModuleJobDispatcher> logger, ScheduledTenantContext? executionTenant=null)
{
    public async Task ExecuteAsync(long runId, CancellationToken ct)
    {
        var candidate = await db.Runs.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.Id == runId, ct);
        if (candidate == null) return;
        var definition = await db.Definitions.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == candidate.ScheduleId && x.TenantId == candidate.TenantId, ct);
        if (definition == null) return;
        var handler = handlers.SingleOrDefault(x => x.Info.Key == definition.Handler);
        // Also prevents two different definitions of the same tenant/handler overlapping.
        await using var lease = await SchedulerSqlLease.TryAcquireAsync(storage.SchedulerConnection,
            $"AccountsScheduler:Handler:{definition.TenantId}:{handler?.Info.ConcurrencyKey??definition.Handler}", ct);
        if (lease == null) return;
        var current = await db.Definitions.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == definition.Id && x.TenantId == definition.TenantId, ct);
        if (current == null || current.Handler != definition.Handler) return;
        definition = current;
        var run = await db.Runs.IgnoreQueryFilters().SingleAsync(x => x.Id == runId && x.TenantId == definition.TenantId, ct);
        var now = DateTime.UtcNow;
        var recovering = run.Status == "Running" && run.LeaseUntilUtc <= now;
        if (!recovering && (run.Status is not ("Queued" or "RetryScheduled") || run.NextAttemptUtc > now)) return;
        if(handler==null||handler.Info.SystemOnly!=(definition.TenantId==0))
        {
            run.Status="Failed";run.Error="The handler is unavailable or has an invalid system/tenant scope.";
            run.FinishedUtc=now;run.NextAttemptUtc=null;run.LeaseUntilUtc=null;await db.SaveChangesAsync(ct);return;
        }
        if (!run.IsManual && (!definition.Enabled || run.ScheduleRevision != definition.Revision))
        {
            run.Status = "Cancelled"; run.Result = "Schedule was disabled or changed before execution.";
            run.FinishedUtc = now; run.NextAttemptUtc = null; await db.SaveChangesAsync(ct); return;
        }
        if (recovering && run.Attempt > definition.RetryCount)
        {
            run.Status = "Failed"; run.Error = "Previous worker stopped; retry limit reached. Review before running again.";
            run.FinishedUtc = now; run.LeaseUntilUtc = null; await db.SaveChangesAsync(ct); return;
        }
        run.Status = "Running"; run.Attempt++; run.StartedUtc = now; run.FinishedUtc = null;
        run.NextAttemptUtc = null; run.Error = null;
        run.LeaseUntilUtc = now.AddSeconds(definition.TimeoutSeconds + 30);
        await db.SaveChangesAsync(ct);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(definition.TimeoutSeconds));
        try
        {
            if (definition.TenantId>0)
            {
                await using var connection=new Microsoft.Data.SqlClient.SqlConnection(storage.BusinessConnection);
                await connection.OpenAsync(timeout.Token);
                await using var command=connection.CreateCommand();
                command.CommandText="SELECT COUNT(*) FROM dbo.Tenants WHERE Id=@tenant AND IsActive=1";
                command.Parameters.AddWithValue("@tenant",definition.TenantId);
                if(Convert.ToInt32(await command.ExecuteScalarAsync(timeout.Token))!=1)
                    throw new InvalidOperationException("The scheduled tenant is inactive or unavailable.");
            }
            if(executionTenant!=null)executionTenant.Id=definition.TenantId;
            run.Result = await handler.ExecuteAsync(definition.TenantId, timeout.Token);
            run.Status = "Succeeded";
        }
        catch (Exception ex)
        {
            run.Error = ex is OperationCanceledException ? "Execution was cancelled or timed out." : ex.Message[..Math.Min(ex.Message.Length, 1000)];
            var retry = !ct.IsCancellationRequested && run.Attempt <= definition.RetryCount;
            run.Status = retry ? "RetryScheduled" : ct.IsCancellationRequested ? "Cancelled" : "Failed";
            run.NextAttemptUtc = retry ? DateTime.UtcNow.AddMinutes(run.Attempt) : null;
            logger.LogWarning("Job {Id} tenant {Tenant}: {Status}", definition.Id, definition.TenantId, run.Status);
        }
        finally
        {
            run.FinishedUtc = DateTime.UtcNow; run.LeaseUntilUtc = null;
            await db.SaveChangesAsync(CancellationToken.None);
        }
    }
}
