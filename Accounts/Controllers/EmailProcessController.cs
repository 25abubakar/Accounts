using System.Security.Claims;
using Accounts.DTOs;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Accounts.Controllers;

[ApiController]
[Authorize]
[Route("api/email/lt-email")]
[Route("LTEmailProcess")]
public sealed class LTEmailProcessController : ControllerBase
{
    private readonly ILTEmailService _emailService;
    private readonly ITenantService _tenantService;
    private readonly IWebHostEnvironment _env;

    public LTEmailProcessController(
        ILTEmailService emailService,
        ITenantService tenantService,
        IWebHostEnvironment env)
    {
        _emailService = emailService;
        _tenantService = tenantService;
        _env = env;
    }

    private string CurrentUserHandle =>
        User.Identity?.Name ??
        User.FindFirstValue(ClaimTypes.NameIdentifier) ??
        "unknown";

    private int? CurrentTenantId =>
        _tenantService.TenantId;

    /// <summary>
    /// Gets received emails for the current logged-in user.
    /// Dual routed to: GET /api/email/lt-email/inbox AND GET /LTEmailProcess/inbox (or /Inbox)
    /// </summary>
    [HttpGet("inbox")]
    public async Task<IActionResult> Inbox(CancellationToken ct)
    {
        var messages = await _emailService.GetLogInUserEmailsAsync(CurrentUserHandle, CurrentTenantId, ct);
        return Ok(messages);
    }

    /// <summary>
    /// Gets sent emails by the current logged-in user.
    /// Dual routed to: GET /api/email/lt-email/sent AND GET /LTEmailProcess/SentEmail
    /// </summary>
    [HttpGet("sent")]
    [HttpGet("SentEmail")]
    public async Task<IActionResult> SentEmail(CancellationToken ct)
    {
        var messages = await _emailService.GetLogInUserSentEmailsAsync(CurrentUserHandle, CurrentTenantId, ct);
        return Ok(messages);
    }

    /// <summary>
    /// Sends a new email with optional attachments.
    /// Dual routed to: POST /api/email/lt-email/compose AND POST /LTEmailProcess/compose (or /Compose)
    /// </summary>
    [HttpPost("compose")]
    [RequestSizeLimit(50L * 1024 * 1024)]
    public async Task<IActionResult> Compose(
        [FromForm] tblMessage message,
        [FromForm] List<IFormFile>? attachments,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(message.ReceiverEmail))
        {
            return BadRequest(new { message = "Recipient email/identifier is required." });
        }

        var result = await _emailService.ComposeEmailAsync(
            message,
            attachments,
            CurrentUserHandle,
            CurrentTenantId,
            ct);

        return Ok(new
        {
            message = result,
            id = message.Id,
            status = "success"
        });
    }

    /// <summary>
    /// Sends a new email via JSON payload (without multipart file uploads).
    /// Endpoint: POST /api/email/lt-email/send-json
    /// </summary>
    [HttpPost("send-json")]
    public async Task<IActionResult> SendJson(
        [FromBody] SendEmailJsonRequest dto,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.ReceiverEmail))
        {
            return BadRequest(new { message = "Recipient email/identifier is required." });
        }

        var message = new tblMessage
        {
            ReceiverEmail = dto.ReceiverEmail,
            Subject = dto.Subject,
            Body = dto.Body
        };

        var result = await _emailService.ComposeEmailAsync(
            message,
            null,
            CurrentUserHandle,
            CurrentTenantId,
            ct);

        return Ok(new
        {
            message = result,
            id = message.Id,
            status = "success"
        });
    }

    /// <summary>
    /// Marks an email as read/opened.
    /// Dual routed to: POST /api/email/lt-email/opened/{id} AND POST /LTEmailProcess/OpenedEmail
    /// </summary>
    [HttpPost("opened/{id:int}")]
    [HttpPost("OpenedEmail")]
    public async Task<IActionResult> OpenedEmail(
        [FromRoute] int? id,
        [FromForm] int? formId,
        [FromQuery] int? queryId,
        CancellationToken ct)
    {
        var targetId = id ?? formId ?? queryId;
        if (!targetId.HasValue || targetId.Value <= 0)
        {
            return BadRequest(new { message = "Valid email ID is required." });
        }

        var result = await _emailService.OpenedEmailAsync(targetId.Value, CurrentUserHandle, ct);
        return Ok(new { result });
    }

    /// <summary>
    /// Searches active staff for recipient auto-complete.
    /// Dual routed to: GET /api/email/lt-email/suggestions AND GET /LTEmailProcess/SearchEmailSuggestions
    /// </summary>
    [HttpGet("suggestions")]
    [HttpGet("SearchEmailSuggestions")]
    public async Task<IActionResult> SearchEmailSuggestions([FromQuery] string? prefix, CancellationToken ct)
    {
        var results = await _emailService.SearchEmailSuggestionsAsync(prefix ?? string.Empty, CurrentTenantId, ct);
        return Ok(results);
    }

    /// <summary>
    /// Gets single email message details with attachments.
    /// </summary>
    [HttpGet("messages/{id:int}")]
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetMessage(int id, CancellationToken ct)
    {
        var message = await _emailService.GetEmailDetailsAsync(id, CurrentUserHandle, ct);
        if (message == null)
        {
            return NotFound(new { message = "Email message not found." });
        }

        return Ok(message);
    }

    /// <summary>
    /// Secure file download for email attachments.
    /// </summary>
    [HttpGet("download/{fileName}")]
    public IActionResult DownloadAttachment(string fileName)
    {
        var root = _env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
        var filePath = Path.Combine(root, "EmailDocuments", fileName);

        if (!System.IO.File.Exists(filePath))
        {
            return NotFound(new { message = "Attachment file not found." });
        }

        var contentType = "application/octet-stream";
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        contentType = extension switch
        {
            ".pdf" => "application/pdf",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".zip" => "application/zip",
            _ => "application/octet-stream"
        };

        return PhysicalFile(filePath, contentType, fileName);
    }
}
