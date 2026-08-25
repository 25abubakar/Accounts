using Accounts.DTOs;
using Accounts.Services.Interfaces;
using Accounts.Services.Services;

namespace Accounts.Services;

public sealed class AttendanceFinalizationScheduler(
    IServiceScopeFactory scopeFactory,
    IRealtimePublisher realtime,
    ILogger<AttendanceFinalizationScheduler> logger) : BackgroundService
{
    private readonly SemaphoreSlim _runGate = new(1, 1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await RunNowAsync(stoppingToken);
            using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await RunNowAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    public async Task RunNowAsync(CancellationToken cancellationToken = default)
    {
        if (!await _runGate.WaitAsync(0, cancellationToken))
            return;

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<AttendanceFinalizationService>();
            var changesByTenant = await service.RefreshCurrentPeriodsAsync(cancellationToken);
            foreach (var change in changesByTenant)
            {
                logger.LogInformation(
                    "Attendance finalizer created or updated {Count} daily rows for tenant {TenantId}.",
                    change.Value,
                    change.Key);
                await realtime.PublishEventToTenantAsync(
                    change.Key,
                    RealtimeEventDto.Create(
                        RealtimeEventTypes.DeductionChanged,
                        "deduction",
                        "attendance-finalized",
                        change.Key,
                        data: new Dictionary<string, string>
                        {
                            ["year"] = PakistanClock.Today().Year.ToString(),
                            ["month"] = PakistanClock.Today().Month.ToString()
                        }));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Hourly attendance finalization failed.");
        }
        finally
        {
            _runGate.Release();
        }
    }
}
