using Accounts.Idempotency;
namespace Accounts.Services.Scheduler.Jobs;

public sealed class IdempotencyExpiryJobService(IServiceScopeFactory scopes, TimeProvider timeProvider,
    ILogger<IdempotencyExpiryJobService> logger)
{
    public async Task<int> RunAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IIdempotencyStore>();
        var count = await store.DeleteExpiredAsync(timeProvider.GetUtcNow().UtcDateTime, ct);
        logger.LogInformation("Deleted {Count} expired idempotency records.", count);
        return count;
    }
}
