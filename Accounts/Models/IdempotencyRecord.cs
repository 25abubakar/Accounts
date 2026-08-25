namespace Accounts.Models;

public enum IdempotencyRecordStatus : byte
{
    Processing = 1,
    Completed = 2,
    Failed = 3
}

public sealed class IdempotencyRecord
{
    public Guid Id { get; set; }
    public Guid IdempotencyKey { get; set; }
    public byte[] ScopeHash { get; set; } = [];
    public byte[] RequestHash { get; set; } = [];
    public int? TenantId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string HttpMethod { get; set; } = string.Empty;
    public string RequestPath { get; set; } = string.Empty;
    public IdempotencyRecordStatus Status { get; set; }
    public Guid LockToken { get; set; }
    public DateTime LeaseExpiresUtc { get; set; }
    public DateTime ExpiresUtc { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
    public int? ResponseStatusCode { get; set; }
    public string? ResponseContentType { get; set; }
    public string? ResponseHeadersJson { get; set; }
    public byte[]? ResponseBody { get; set; }
    public string? FailureReason { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
