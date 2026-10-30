using Accounts.Models;
using Microsoft.AspNetCore.Http;

namespace Accounts.DTOs;

public sealed record EmailMessageDto(
    int Id,
    string? SenderEmail,
    string? ReceiverEmail,
    string? Subject,
    string? Body,
    DateTime? SentAt,
    bool? isRead,
    int? OrganizationId,
    List<EmailAttachmentDto> Attachments);

public sealed record EmailAttachmentDto(
    int Id,
    int? EmailId,
    string? Attachment,
    string DownloadUrl);

public sealed class ComposeEmailRequest
{
    public string? ReceiverEmail { get; set; }
    public string? Subject { get; set; }
    public string? Body { get; set; }
    public List<IFormFile> Attachments { get; set; } = new();
}

public sealed class SendEmailJsonRequest
{
    public string ReceiverEmail { get; set; } = string.Empty;
    public string? Subject { get; set; }
    public string? Body { get; set; }
}

public sealed record StaffEmailSuggestionDto(
    Guid? PersonId,
    Guid? StaffId,
    string FullName,
    string LtNo,
    string? Email,
    string? Department,
    string? Designation);
