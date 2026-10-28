using Accounts.Data;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Scheduler;

public sealed class ScheduleSynchronizer(IServiceScopeFactory scopes)
{
    public async Task ApplyAsync(Guid id, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SchedulerDbContext>();
        var value = await db.Definitions.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (value == null || value.RuntimeRevision == value.Revision) return;
        value.NextRunUtc = value.Enabled ? ScheduleTiming.FirstDue(value, DateTime.UtcNow) : null;
        value.RuntimeRevision = value.Revision;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { /* The next service cycle reads the latest revision. */ }
    }
}
