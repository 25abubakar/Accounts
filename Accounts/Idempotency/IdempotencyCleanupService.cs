using Microsoft.Extensions.Options;

namespace Accounts.Idempotency;

public sealed class IdempotencyCleanupService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    IOptions<IdempotencyOptions> options,
    ILogger<IdempotencyCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(options.Value.CleanupIntervalMinutes);
        using var timer = new PeriodicTimer(interval, timeProvider);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var store = scope.ServiceProvider.GetRequiredService<IIdempotencyStore>();
                var deleted = await store.DeleteExpiredAsync(
                    timeProvider.GetUtcNow().UtcDateTime,
                    stoppingToken);
                if (deleted > 0)
                    logger.LogInformation("Deleted {Count} expired idempotency records.", deleted);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Idempotency cleanup failed.");
            }
        }
    }
}
