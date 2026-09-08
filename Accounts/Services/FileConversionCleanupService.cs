using Microsoft.Extensions.Options;

namespace Accounts.Services;

public sealed class FileConversionCleanupService(
    IWebHostEnvironment environment,
    IOptions<FileConversionOptions> options,
    ILogger<FileConversionCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await CleanupAsync(stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(15));
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await CleanupAsync(stoppingToken);
    }

    private Task CleanupAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var root = Path.Combine(environment.ContentRootPath, "App_Data", "file-conversion-temp");
        if (!Directory.Exists(root)) return Task.CompletedTask;

        var cutoff = DateTime.UtcNow.AddMinutes(-Math.Max(5, options.Value.TemporaryFileRetentionMinutes));
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (Directory.GetLastWriteTimeUtc(directory) < cutoff)
                    Directory.Delete(directory, recursive: true);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Could not clean stale conversion directory {Path}.", directory);
            }
        }
        return Task.CompletedTask;
    }
}
