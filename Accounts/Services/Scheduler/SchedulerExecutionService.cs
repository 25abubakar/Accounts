namespace Accounts.Services.Scheduler;

// Registered only by the dedicated Windows Worker host, never by the Accounts API.
public sealed class SchedulerExecutionService(SchedulerQueue queue, IServiceScopeFactory scopes,
    IConfiguration configuration, ILogger<SchedulerExecutionService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollSeconds = Math.Clamp(configuration.GetValue("Scheduler:PollSeconds", 5), 2, 60);
        var concurrency = Math.Clamp(configuration.GetValue("Scheduler:Concurrency", 4), 1, 16);
        logger.LogInformation("Accounts Scheduler worker started. SQL queue polling: {Seconds}s; concurrency: {Concurrency}.", pollSeconds, concurrency);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var runs = await queue.CollectAsync(stoppingToken);
                    foreach (var batch in runs.Chunk(concurrency))
                    {
                        if (stoppingToken.IsCancellationRequested) break;
                        // Finish this bounded batch on a normal Windows service Stop.
                        await Task.WhenAll(batch.Select(async id =>
                        {
                            await using var scope = scopes.CreateAsyncScope();
                            try { await scope.ServiceProvider.GetRequiredService<ModuleJobDispatcher>().ExecuteAsync(id, CancellationToken.None); }
                            catch (Exception ex) { logger.LogError(ex, "Execution {RunId} interrupted; SQL lease/retry recovery will handle it.", id); }
                        }));
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex) { logger.LogError(ex, "Scheduler queue unavailable. No job was started by this cycle."); }
                await Task.Delay(TimeSpan.FromSeconds(pollSeconds), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally { logger.LogInformation("Accounts Scheduler worker stopped; current batch drained."); }
    }
}
