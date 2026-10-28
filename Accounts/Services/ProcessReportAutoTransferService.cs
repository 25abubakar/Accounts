using Accounts.Data;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services;

public sealed class ProcessReportAutoTransferService(
    IServiceScopeFactory scopeFactory,
    ILogger<ProcessReportAutoTransferService> logger)
{
    public async Task RunAsync(CancellationToken ct)
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
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            logger.LogError(error, "Unable to auto-transfer overdue process reports.");
            throw;
        }
    }
}
