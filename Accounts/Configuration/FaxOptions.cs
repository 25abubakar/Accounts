namespace Accounts.Configuration;

public sealed class FaxOptions
{
    public const string SectionName = "Fax";
    public bool Enabled { get; set; } = true;
    public string Provider { get; set; } = "Sandbox";
    public string BaseUrl { get; set; } = "https://api.phaxio.com/v2.1/";
    public string? ApiKey { get; set; }
    public string? ApiSecret { get; set; }
    public string? WebhookSecret { get; set; }
    public string? CallbackUrl { get; set; }
    public int RequestTimeoutSeconds { get; set; } = 60;
    public int MaximumFileCount { get; set; } = 10;
    public long MaximumFileSizeBytes { get; set; } = 20L * 1024 * 1024;
    public long MaximumTotalSizeBytes { get; set; } = 50L * 1024 * 1024;
    public long MaximumPreviewCacheBytes { get; set; } = 256L * 1024 * 1024;
    public string[] AllowedExtensions { get; set; } = [".pdf"];
    public int ReconciliationMinimumAgeMinutes { get; set; } = 5;
    public int ReconciliationBatchSize { get; set; } = 50;
}
