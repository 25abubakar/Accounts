using Accounts.DTOs;
using Accounts.Models;

namespace Accounts.Services.Interfaces;

public interface IFaxDocumentService
{
    FaxPreparedDocument Prepare(FaxSendMetadataDto metadata, FaxRecipient recipient, IReadOnlyList<FaxUpload> uploads);
    string CreateConfirmationToken(FaxPreparedDocument document);
    FaxPreparedDocument? ResolveConfirmationToken(string token, FaxPreparedDocument expectedDocument);
}

public interface IFaxPreviewStore
{
    string Store(FaxPreparedDocument document, DateTime expiresAtUtc);
    FaxPreparedDocument? Get(string key, string expectedFingerprint, DateTime nowUtc);
}

public sealed record FaxProviderSubmission(string Provider,string ProviderFaxId,string Status);
public sealed record FaxProviderState(string Status,string? FailureCode=null,string? FailureSummary=null);
public interface IFaxProvider
{
    string Name { get; }
    bool IsConfigured { get; }
    Task<FaxProviderSubmission> SubmitAsync(string destination,string reference,byte[] pdf,CancellationToken ct);
    Task<FaxProviderState> GetStatusAsync(string providerFaxId,CancellationToken ct);
}

public interface IFaxStatusService
{
    Task TransitionAsync(FaxTransmission transmission,string status,string source,string? detail,string? providerEventReference,string? actorUserId,CancellationToken ct);
}

public interface IFaxDispatchService
{
    Task<(byte[] Pdf,string ConfirmationToken)> PreviewAsync(FaxSendMetadataDto metadata,IReadOnlyList<FaxUpload> uploads,CancellationToken ct);
    Task<FaxTransmissionDto> SendAsync(FaxSendMetadataDto metadata,IReadOnlyList<FaxUpload> uploads,Guid idempotencyKey,string confirmationToken,string userId,CancellationToken ct);
    Task<FaxTransmissionDto?> ReconcileAsync(Guid id,string? actorUserId,CancellationToken ct);
    Task<int> ReconcilePendingAsync(int tenantId,CancellationToken ct);
}
