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
            // Normal host shutdown. BackgroundService must complete quietly so
            // Visual Studio does not report an unhandled cancellation exception.
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.ExecuteSqlRawAsync("EXEC dbo.usp_ProcessReport_AutoTransferOverdue", ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception error)
        {
            logger.LogError(error, "Unable to auto-transfer overdue process reports.");
        }
    }
}
