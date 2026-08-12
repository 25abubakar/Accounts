using Accounts.Data;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services;

public sealed class ProcessReportAutoTransferService(
    IServiceScopeFactory scopeFactory,
    ILogger<ProcessReportAutoTransferService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await RunAsync(stoppingToken);
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await RunAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutdown and debugger restarts cancel PeriodicTimer by design.
            // Treat that cancellation as a clean worker stop, not an application error.
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            // The deployed procedure owns its timeout policy. Keeping the worker call
            // parameterless also supports databases upgraded from the original
            // zero-parameter procedure without generating an error every five minutes.
            await db.Database.ExecuteSqlRawAsync(
                "EXEC dbo.usp_ProcessReport_AutoTransferOverdue",
                ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception error)
        {
            logger.LogError(error, "Unable to auto-transfer overdue process reports.");
        }
    }
}
