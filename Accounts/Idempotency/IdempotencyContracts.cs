using Accounts.Models;

namespace Accounts.Idempotency;

public sealed record IdempotencyCommand(
    Guid Key,
    byte[] ScopeHash,
    byte[] RequestHash,
    int? TenantId,
    string UserId,
    string HttpMethod,
    string RequestPath,
    DateTime NowUtc,
    DateTime LeaseExpiresUtc,
    DateTime ExpiresUtc);

public sealed record StoredIdempotencyResponse(
    int StatusCode,
    string? ContentType,
    string? HeadersJson,
    byte[] Body);

public enum IdempotencyBeginState
{
    Acquired,
    Processing,
    Completed,
    PayloadMismatch,
    Failed
}

public sealed record IdempotencyBeginResult(
    IdempotencyBeginState State,
    Guid? RecordId = null,
    Guid? LockToken = null,
    DateTime? LeaseExpiresUtc = null,
    StoredIdempotencyResponse? Response = null,
    string? FailureReason = null);

public interface IIdempotencyStore
{
    Task<IdempotencyBeginResult> BeginAsync(IdempotencyCommand command, CancellationToken cancellationToken);

    Task CompleteAsync(
        Guid recordId,
        Guid lockToken,
        StoredIdempotencyResponse response,
        DateTime completedUtc,
        CancellationToken cancellationToken);

    Task FailAsync(
        Guid recordId,
        Guid lockToken,
        string reason,
        bool release,
        DateTime failedUtc,
        CancellationToken cancellationToken);

    Task<int> DeleteExpiredAsync(DateTime utcNow, CancellationToken cancellationToken);
}
