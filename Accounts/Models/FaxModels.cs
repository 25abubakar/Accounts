using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models;

[Table("FaxRecipients")]
public sealed class FaxRecipient : ITenantEntity
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    public int TenantId { get; set; }
    [MaxLength(160)] public string Name { get; set; } = string.Empty;
    [MaxLength(200)] public string OrganizationName { get; set; } = string.Empty;
    [MaxLength(120)] public string? Department { get; set; }
    [MaxLength(32)] public string FaxNumber { get; set; } = string.Empty;
    [MaxLength(32)] public string NormalizedFaxNumber { get; set; } = string.Empty;
    public bool IsVerified { get; set; }
    [MaxLength(450)] public string? VerifiedByUserId { get; set; }
    public DateTime? VerifiedAtUtc { get; set; }
    public bool IsActive { get; set; } = true;
    [MaxLength(450)] public string CreatedByUserId { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }
    [Timestamp] public byte[] RowVersion { get; set; } = [];
}

[Table("FaxTransmissions")]
public sealed class FaxTransmission : ITenantEntity
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    public int TenantId { get; set; }
    public int? OrganizationId { get; set; }
    [MaxLength(32)] public string Reference { get; set; } = string.Empty;
    [MaxLength(120)] public string? PatientExternalId { get; set; }
    [MaxLength(200)] public string? PatientDisplayName { get; set; }
    [MaxLength(120)] public string? AppointmentExternalId { get; set; }
    public Guid RecipientId { get; set; }
    public FaxRecipient Recipient { get; set; } = null!;
    [MaxLength(160)] public string RecipientName { get; set; } = string.Empty;
    [MaxLength(200)] public string RecipientOrganization { get; set; } = string.Empty;
    [MaxLength(32)] public string RecipientFaxNumber { get; set; } = string.Empty;
    [MaxLength(160)] public string Subject { get; set; } = string.Empty;
    [MaxLength(32)] public string Status { get; set; } = FaxStatuses.Preparing;
    [MaxLength(32)] public string Provider { get; set; } = string.Empty;
    [MaxLength(160)] public string? ProviderFaxId { get; set; }
    public Guid IdempotencyKey { get; set; }
    public Guid? ResubmissionOfId { get; set; }
    public int AttachmentCount { get; set; }
    public int? PageCount { get; set; }
    [MaxLength(450)] public string CreatedByUserId { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? AcceptedAtUtc { get; set; }
    public DateTime? DeliveredAtUtc { get; set; }
    public DateTime? LastCheckedAtUtc { get; set; }
    [MaxLength(80)] public string? FailureCode { get; set; }
    [MaxLength(500)] public string? FailureSummary { get; set; }
    [Timestamp] public byte[] RowVersion { get; set; } = [];
    public ICollection<FaxAttachmentMetadata> Attachments { get; set; } = [];
    public ICollection<FaxStatusEvent> Events { get; set; } = [];
}

[Table("FaxAttachmentMetadata")]
public sealed class FaxAttachmentMetadata : ITenantEntity
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    public int TenantId { get; set; }
    public Guid TransmissionId { get; set; }
    public FaxTransmission Transmission { get; set; } = null!;
    [MaxLength(220)] public string DisplayName { get; set; } = string.Empty;
    [MaxLength(100)] public string ContentType { get; set; } = "application/pdf";
    public long SizeBytes { get; set; }
    [MaxLength(64)] public string Sha256 { get; set; } = string.Empty;
    public int? PageCount { get; set; }
    public int SortOrder { get; set; }
}

[Table("FaxStatusEvents")]
public sealed class FaxStatusEvent : ITenantEntity
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    public int TenantId { get; set; }
    public Guid TransmissionId { get; set; }
    public FaxTransmission Transmission { get; set; } = null!;
    [MaxLength(32)] public string? PreviousStatus { get; set; }
    [MaxLength(32)] public string Status { get; set; } = string.Empty;
    [MaxLength(32)] public string Source { get; set; } = "Application";
    [MaxLength(160)] public string? ProviderEventReference { get; set; }
    [MaxLength(500)] public string? Detail { get; set; }
    [MaxLength(450)] public string? ActorUserId { get; set; }
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
}

public static class FaxStatuses
{
    public const string Preparing="Preparing", Submitting="Submitting", Accepted="Accepted", Transmitting="Transmitting",
        Delivered="Delivered", Failed="Failed", AcceptanceUnknown="AcceptanceUnknown", ReconciliationRequired="ReconciliationRequired";
    public static readonly IReadOnlySet<string> Terminal = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Delivered, Failed };
}
