using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace Accounts.Scheduler.Worker;

// Development-only control channel, restricted by Windows to this worker's user.
public sealed class LocalControlService(IHostApplicationLifetime lifetime, ILogger<LocalControlService> logger) : BackgroundService
{
    public static string PipeName => "AccountsScheduler-" + Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(WindowsIdentity.GetCurrent().User!.Value))).ToLowerInvariant()[..16];
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(stoppingToken);
                using var reader = new StreamReader(pipe);
                if (await reader.ReadLineAsync(stoppingToken) == "stop")
                {
                    logger.LogInformation("Local owner requested graceful worker stop.");
                    lifetime.StopApplication();
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
