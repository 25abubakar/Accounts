namespace Accounts.Idempotency;

public sealed class IdempotencyOptions
{
    public const string SectionName = "Idempotency";

    public string HeaderName { get; set; } = "X-Idempotency-Key";
    public int DefaultTtlHours { get; set; } = 24;
    public int ProcessingLeaseSeconds { get; set; } = 120;
    public int MaxRequestBodyBytes { get; set; } = 1_048_576;
    public int MaxResponseBodyBytes { get; set; } = 2_097_152;
    public int CleanupIntervalMinutes { get; set; } = 30;
    public string[] ReplayHeaders { get; set; } = ["Content-Language", "ETag", "Location"];
}
