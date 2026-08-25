using System.Data;
using System.Security.Cryptography;
using Accounts.Data;
using Accounts.Models;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Idempotency;

public sealed class EfIdempotencyStore(
    ApplicationDbContext db) : IIdempotencyStore
{
    public Task<IdempotencyBeginResult> BeginAsync(
        IdempotencyCommand command,
        CancellationToken cancellationToken)
    {
        var executionStrategy = db.Database.CreateExecutionStrategy();
        return executionStrategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

            var record = await db.IdempotencyRecords.SingleOrDefaultAsync(
                item => item.IdempotencyKey == command.Key && item.ScopeHash == command.ScopeHash,
                cancellationToken);

            IdempotencyBeginResult result;
            if (record is null)
            {
                record = CreateRecord(command);
                db.IdempotencyRecords.Add(record);
                await db.SaveChangesAsync(cancellationToken);
                result = Acquired(record);
            }
            else if (record.ExpiresUtc <= command.NowUtc)
            {
                ResetForNewExecution(record, command);
                await db.SaveChangesAsync(cancellationToken);
                result = Acquired(record);
            }
            else if (!CryptographicOperations.FixedTimeEquals(record.RequestHash, command.RequestHash))
            {
                result = new(IdempotencyBeginState.PayloadMismatch);
            }
            else
            {
                result = record.Status switch
                {
                    IdempotencyRecordStatus.Completed when record.ResponseStatusCode.HasValue =>
                        new(
                            IdempotencyBeginState.Completed,
                            record.Id,
                            Response: new StoredIdempotencyResponse(
                                record.ResponseStatusCode.Value,
                                record.ResponseContentType,
                                record.ResponseHeadersJson,
                                record.ResponseBody ?? [])),
                    IdempotencyRecordStatus.Failed =>
                        new(
                            IdempotencyBeginState.Failed,
                            record.Id,
                            LeaseExpiresUtc: record.LeaseExpiresUtc,
                            FailureReason: record.FailureReason),
                    _ =>
                        new(
                            IdempotencyBeginState.Processing,
                            record.Id,
                            LeaseExpiresUtc: record.LeaseExpiresUtc)
                };
            }

            await transaction.CommitAsync(cancellationToken);
            if (record is not null)
                db.Entry(record).State = EntityState.Detached;
            return result;
        });
    }

    public async Task CompleteAsync(
        Guid recordId,
        Guid lockToken,
        StoredIdempotencyResponse response,
        DateTime completedUtc,
        CancellationToken cancellationToken)
    {
        var updated = await db.IdempotencyRecords
            .Where(item =>
                item.Id == recordId &&
                item.LockToken == lockToken &&
                item.Status == IdempotencyRecordStatus.Processing)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, IdempotencyRecordStatus.Completed)
                .SetProperty(item => item.ResponseStatusCode, response.StatusCode)
                .SetProperty(item => item.ResponseContentType, response.ContentType)
                .SetProperty(item => item.ResponseHeadersJson, response.HeadersJson)
                .SetProperty(item => item.ResponseBody, response.Body)
                .SetProperty(item => item.CompletedUtc, completedUtc)
                .SetProperty(item => item.UpdatedUtc, completedUtc)
                .SetProperty(item => item.FailureReason, (string?)null),
                cancellationToken);

        if (updated != 1)
            throw new InvalidOperationException(
                "The idempotency lease was lost before the response could be committed.");
    }

    public async Task FailAsync(
        Guid recordId,
        Guid lockToken,
        string reason,
        bool release,
        DateTime failedUtc,
        CancellationToken cancellationToken)
    {
        var query = db.IdempotencyRecords.Where(item =>
            item.Id == recordId &&
            item.LockToken == lockToken &&
            item.Status == IdempotencyRecordStatus.Processing);

        if (release)
        {
            await query.ExecuteDeleteAsync(cancellationToken);
            return;
        }

        var safeReason = string.IsNullOrWhiteSpace(reason)
            ? "The operation did not complete."
            : reason.Trim()[..Math.Min(reason.Trim().Length, 500)];

        await query.ExecuteUpdateAsync(setters => setters
            .SetProperty(item => item.Status, IdempotencyRecordStatus.Failed)
            .SetProperty(item => item.FailureReason, safeReason)
            .SetProperty(item => item.UpdatedUtc, failedUtc),
            cancellationToken);
    }

    public Task<int> DeleteExpiredAsync(DateTime utcNow, CancellationToken cancellationToken) =>
        db.IdempotencyRecords
            .Where(item => item.ExpiresUtc <= utcNow)
            .ExecuteDeleteAsync(cancellationToken);

    private static IdempotencyRecord CreateRecord(IdempotencyCommand command) =>
        new()
        {
            Id = Guid.NewGuid(),
            IdempotencyKey = command.Key,
            ScopeHash = command.ScopeHash,
            RequestHash = command.RequestHash,
            TenantId = command.TenantId,
            UserId = command.UserId,
            HttpMethod = command.HttpMethod,
            RequestPath = command.RequestPath,
            Status = IdempotencyRecordStatus.Processing,
            LockToken = Guid.NewGuid(),
            LeaseExpiresUtc = command.LeaseExpiresUtc,
            ExpiresUtc = command.ExpiresUtc,
            CreatedUtc = command.NowUtc,
            UpdatedUtc = command.NowUtc
        };

    private static void ResetForNewExecution(
        IdempotencyRecord record,
        IdempotencyCommand command)
    {
        record.RequestHash = command.RequestHash;
        record.TenantId = command.TenantId;
        record.UserId = command.UserId;
        record.HttpMethod = command.HttpMethod;
        record.RequestPath = command.RequestPath;
        record.Status = IdempotencyRecordStatus.Processing;
        record.LockToken = Guid.NewGuid();
        record.LeaseExpiresUtc = command.LeaseExpiresUtc;
        record.ExpiresUtc = command.ExpiresUtc;
        record.CreatedUtc = command.NowUtc;
        record.UpdatedUtc = command.NowUtc;
        record.CompletedUtc = null;
        record.ResponseStatusCode = null;
        record.ResponseContentType = null;
        record.ResponseHeadersJson = null;
        record.ResponseBody = null;
        record.FailureReason = null;
    }

    private static IdempotencyBeginResult Acquired(IdempotencyRecord record) =>
        new(
            IdempotencyBeginState.Acquired,
            record.Id,
            record.LockToken,
            record.LeaseExpiresUtc);
}
