using Accounts.Data;
using Accounts.Models.Scheduler;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Scheduler.Worker;

public sealed class WorkerStartupValidation(IServiceScopeFactory scopes) : IHostedService
{
    // Validation runs after WindowsServiceLifetime connects to SCM, avoiding an SQL
    // connection attempt delaying the initial Windows service handshake.
    public Task StartAsync(CancellationToken ct) => CheckAsync(scopes, false, ct);
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    public static async Task CheckAsync(IServiceScopeFactory scopes, bool migrate, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SchedulerDbContext>();
        if (migrate) await db.Database.MigrateAsync(ct);
        var storage = scope.ServiceProvider.GetRequiredService<SchedulerStorage>();
        await using var business = new SqlConnection(storage.BusinessConnection);
        await business.OpenAsync(ct);
        if (!await db.Database.CanConnectAsync(ct) || (await db.Database.GetPendingMigrationsAsync(ct)).Any())
            throw new InvalidOperationException("Scheduler database is unavailable or migrations are pending. Run --migrate before starting.");
        scope.ServiceProvider.GetRequiredService<ILogger<WorkerStartupValidation>>()
            .LogInformation("Accounts Scheduler SQL connectivity and schema checks passed.");
    }
}
