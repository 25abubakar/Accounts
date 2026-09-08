namespace Accounts.Services;

public sealed class FileConversionOptions
{
    public const string SectionName = "FileConversion";

    public bool Enabled { get; set; } = true;
    public string? LibreOfficeExecutablePath { get; set; }
    public int MaximumFileSizeMb { get; set; } = 25;
    public int MaximumOutputSizeMb { get; set; } = 100;
    public int TimeoutSeconds { get; set; } = 120;
    public int MaximumConcurrentConversions { get; set; } = 2;
    public int TemporaryFileRetentionMinutes { get; set; } = 30;
}
