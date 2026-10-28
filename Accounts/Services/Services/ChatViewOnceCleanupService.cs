using Accounts.Data;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Services;

public sealed class ChatViewOnceCleanupService(
    IServiceScopeFactory scopeFactory,
    IWebHostEnvironment environment,
    ILogger<ChatViewOnceCleanupService> logger)
{
    public async Task RunAsync(CancellationToken cancellationToken)
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
        var privateRoot = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "App_Data", "chat-uploads")) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(primary).StartsWith(privateRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Chat cleanup path is outside the approved upload directory.");
        if (File.Exists(primary))
        {
            File.Delete(primary);
            return;
        }

        var webRoot = environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot");
        var legacy = Path.Combine(webRoot, "chat-uploads", normalized);
        var legacyRoot = Path.GetFullPath(Path.Combine(webRoot, "chat-uploads")) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(legacy).StartsWith(legacyRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Chat cleanup path is outside the approved legacy directory.");
        if (File.Exists(legacy)) File.Delete(legacy);
    }
}
