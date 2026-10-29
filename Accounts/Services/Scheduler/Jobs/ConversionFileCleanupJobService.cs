using Microsoft.Extensions.Options;

namespace Accounts.Services.Scheduler.Jobs;

public sealed class ConversionFileCleanupJobService(
    IWebHostEnvironment environment,
    IOptions<FileConversionOptions> options,
    ILogger<ConversionFileCleanupJobService> logger)
{
    public Task RunAsync(CancellationToken cancellationToken)
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
                var resolved = Path.GetFullPath(directory);
                var approvedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!resolved.StartsWith(approvedRoot, StringComparison.OrdinalIgnoreCase) ||
                    (File.GetAttributes(resolved) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("Conversion cleanup target is outside the approved directory or is a link.");
                if (Directory.GetLastWriteTimeUtc(resolved) < cutoff)
                    Directory.Delete(resolved, recursive: true);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Could not clean stale conversion directory {Path}.", directory);
            }
        }
        return Task.CompletedTask;
    }
}
