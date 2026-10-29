namespace Accounts.DTOs;

public sealed record FaxBootstrapDto(object Configuration, object Permissions);
public sealed record FaxRecipientDto(Guid Id,string Name,string OrganizationName,string? Department,string FaxNumberMasked,string? FaxNumber,bool IsVerified,DateTime? VerifiedAtUtc,bool IsActive,string RowVersion);
public sealed record SaveFaxRecipientDto(string Name,string OrganizationName,string? Department,string FaxNumber,bool IsVerified,bool IsActive,string? RowVersion);
public sealed record FaxSendMetadataDto(string? PatientExternalId,string? PatientDisplayName,string? AppointmentExternalId,Guid RecipientId,string Subject,string? CoverMessage);
public sealed record FaxTransmissionDto(Guid Id,string Reference,string? PatientExternalId,string? PatientDisplayName,string? AppointmentExternalId,string RecipientName,string RecipientOrganization,string RecipientFaxMasked,string Subject,string Status,string Provider,string? ProviderFaxId,int AttachmentCount,int? PageCount,DateTime CreatedAtUtc,DateTime? AcceptedAtUtc,DateTime? DeliveredAtUtc,DateTime? LastCheckedAtUtc,string? FailureSummary);
public sealed record FaxStatusEventDto(Guid Id,string? PreviousStatus,string Status,string Source,string? Detail,DateTime OccurredAtUtc);
public sealed record FaxAttachmentDto(Guid Id,string DisplayName,string ContentType,long SizeBytes,int? PageCount,int SortOrder);
public sealed record FaxTransmissionDetailDto(FaxTransmissionDto Transmission,IReadOnlyList<FaxAttachmentDto> Attachments,IReadOnlyList<FaxStatusEventDto> Events);
public sealed record FaxUpload(string DisplayName,string ContentType,byte[] Content,string Sha256,int PageCount);
public sealed record FaxPreparedDocument(byte[] Content,string Fingerprint,int PageCount,IReadOnlyList<FaxUpload> Attachments);
