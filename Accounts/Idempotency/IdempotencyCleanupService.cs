namespace Accounts.Idempotency;

public sealed class IdempotencyCleanupService(IServiceScopeFactory scopes, TimeProvider timeProvider,
    ILogger<IdempotencyCleanupService> logger)
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
