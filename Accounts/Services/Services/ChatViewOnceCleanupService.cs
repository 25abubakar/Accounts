using Accounts.Data;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Services;

public sealed class ChatViewOnceCleanupService(
    IServiceScopeFactory scopeFactory,
    IWebHostEnvironment environment,
    ILogger<ChatViewOnceCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ExpireBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "View Once cleanup failed; it will retry on the next cycle.");
            }

            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }

    private async Task ExpireBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var rulesByTenant = await db.ChatRuleSettings
            .IgnoreQueryFilters()
            .AsNoTracking()
            .ToDictionaryAsync(item => item.TenantId, cancellationToken);
        var now = DateTime.UtcNow;
        var candidates = await db.ChatAttachments
            .IgnoreQueryFilters()
            .Where(item =>
                item.IsViewOnce &&
                item.ViewOnceConsumedOnUtc == null &&
                item.ViewOnceExpiredOnUtc == null)
            .OrderBy(item => item.CreatedOnUtc)
            .Take(500)
            .ToListAsync(cancellationToken);

        var expired = candidates.Where(item =>
        {
            var expiryHours = rulesByTenant.TryGetValue(item.TenantId, out var rule)
                ? rule.ViewOnceUnopenedExpiryHours
                : 14 * 24;
            return now >= item.CreatedOnUtc.AddHours(expiryHours);
        }).ToList();
        if (expired.Count == 0) return;

        foreach (var attachment in expired)
        {
            DeletePhysicalFile(attachment.FilePath);
            attachment.FilePath = string.Empty;
            attachment.ViewOnceExpiredOnUtc = now;
        }
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Expired and wiped {Count} unopened View Once chat attachment(s).", expired.Count);
    }

    private void DeletePhysicalFile(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return;
        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
        var primary = Path.Combine(environment.ContentRootPath, "App_Data", "chat-uploads", normalized);
        if (File.Exists(primary))
        {
            File.Delete(primary);
            return;
        }

        var webRoot = environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot");
        var legacy = Path.Combine(webRoot, "chat-uploads", normalized);
        if (File.Exists(legacy)) File.Delete(legacy);
    }
}
