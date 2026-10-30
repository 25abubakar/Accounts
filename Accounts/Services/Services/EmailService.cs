using Accounts.Data;
using Accounts.DTOs;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Accounts.Services.Services;

public sealed class LTEmailService : ILTEmailService
{
    private readonly ApplicationDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<LTEmailService> _logger;

    public LTEmailService(
        ApplicationDbContext db,
        IWebHostEnvironment env,
        ILogger<LTEmailService> logger)
    {
        _db = db;
        _env = env;
        _logger = logger;
    }

    public async Task<List<tblMessage>> GetLogInUserEmailsAsync(string user, int? organizationId, CancellationToken ct = default)
    {
        try
        {
            var handles = await ResolveUserHandlesAsync(user, ct);

            var query = _db.tblMessage.AsNoTracking()
                .Where(x => x.ReceiverEmail != null && handles.Contains(x.ReceiverEmail));

            if (organizationId.HasValue && organizationId.Value > 0)
            {
                query = query.Where(x => x.OrganizationId == organizationId || x.OrganizationId == null);
            }

            var messages = await query.OrderByDescending(x => x.SentAt).Take(1000).ToListAsync(ct);

            if (messages.Count > 0)
            {
                var messageIds = messages.Select(x => (int?)x.Id).ToList();
                var attachments = await _db.tblEmailDocuments.AsNoTracking()
                    .Where(x => x.EmailId.HasValue && messageIds.Contains(x.EmailId))
                    .ToListAsync(ct);

                var attachmentGroup = attachments
                    .GroupBy(x => x.EmailId!.Value)
                    .ToDictionary(g => g.Key, g => g.ToList());

                foreach (var msg in messages)
                {
                    if (attachmentGroup.TryGetValue(msg.Id, out var atts))
                    {
                        msg.Attachments.AddRange(atts);
                    }
                }
            }

            return messages;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get inbox emails for user {User}", user);
            throw;
        }
    }

    public async Task<List<tblMessage>> GetLogInUserSentEmailsAsync(string user, int? organizationId, CancellationToken ct = default)
    {
        try
        {
            var handles = await ResolveUserHandlesAsync(user, ct);

            var query = _db.tblMessage.AsNoTracking()
                .Where(x => x.SenderEmail != null && handles.Contains(x.SenderEmail));

            if (organizationId.HasValue && organizationId.Value > 0)
            {
                query = query.Where(x => x.OrganizationId == organizationId || x.OrganizationId == null);
            }

            var sentMessages = await query.OrderByDescending(x => x.SentAt).Take(1000).ToListAsync(ct);

            if (sentMessages.Count > 0)
            {
                var messageIds = sentMessages.Select(x => (int?)x.Id).ToList();
                var attachments = await _db.tblEmailDocuments.AsNoTracking()
                    .Where(x => x.EmailId.HasValue && messageIds.Contains(x.EmailId))
                    .ToListAsync(ct);

                var attachmentGroup = attachments
                    .GroupBy(x => x.EmailId!.Value)
                    .ToDictionary(g => g.Key, g => g.ToList());

                foreach (var msg in sentMessages)
                {
                    if (attachmentGroup.TryGetValue(msg.Id, out var atts))
                    {
                        msg.Attachments.AddRange(atts);
                    }
                }
            }

            return sentMessages;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get sent emails for user {User}", user);
            throw;
        }
    }

    public async Task<string> ComposeEmailAsync(
        tblMessage message,
        List<IFormFile>? attachments,
        string user,
        int? organizationId,
        CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(user))
                throw new ArgumentException("Sender user identity is required.", nameof(user));

            if (string.IsNullOrWhiteSpace(message.ReceiverEmail))
                throw new ArgumentException("Recipient email or staff identifier is required.", nameof(message.ReceiverEmail));

            var senderHandle = await ResolvePrimarySenderHandleAsync(user, ct);

            message.SentAt = DateTime.UtcNow;
            message.SenderEmail = senderHandle;
            message.OrganizationId = organizationId;
            message.isRead = false;
            message.CreatedBy = user;
            message.CreatedDate = DateTime.UtcNow;

            _db.tblMessage.Add(message);
            await _db.SaveChangesAsync(ct);

            if (attachments != null && attachments.Count > 0)
            {
                var uploads = EnsureUploadDirectory();

                foreach (var attch in attachments)
                {
                    if (attch.Length == 0) continue;

                    var originalName = Path.GetFileNameWithoutExtension(attch.FileName);
                    var extension = Path.GetExtension(attch.FileName);
                    var guid = Guid.NewGuid().ToString();
                    var fileName = $"{originalName}_{guid}{extension}";
                    var filePath = Path.Combine(uploads, fileName);

                    using (var fileStream = new FileStream(filePath, FileMode.Create))
                    {
                        await attch.CopyToAsync(fileStream, ct);
                    }

                    var tblEmailDoc = new tblEmailDocuments
                    {
                        EmailId = message.Id,
                        Attachment = fileName,
                        CreatedBy = user,
                        CreatedDate = DateTime.UtcNow
                    };

                    _db.tblEmailDocuments.Add(tblEmailDoc);
                    message.Attachments.Add(tblEmailDoc);
                }

                await _db.SaveChangesAsync(ct);
            }

            return "Sent..........!";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to compose email from {Sender} to {Receiver}", user, message.ReceiverEmail);
            throw;
        }
    }

    public async Task<string> OpenedEmailAsync(int id, string? user = null, CancellationToken ct = default)
    {
        try
        {
            var data = await _db.tblMessage.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (data != null)
            {
                data.isRead = true;
                data.UpdatedBy = user ?? data.UpdatedBy;
                data.UpdatedDate = DateTime.UtcNow;
                _db.Entry(data).State = EntityState.Modified;
                await _db.SaveChangesAsync(ct);
                return "success";
            }

            return "something went wrong";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to mark email {Id} as opened", id);
            throw;
        }
    }

    public async Task<List<StaffEmailSuggestionDto>> SearchEmailSuggestionsAsync(
        string prefix,
        int? organizationId,
        CancellationToken ct = default)
    {
        try
        {
            var query = from p in _db.Persons.AsNoTracking()
                        join s in _db.StaffVacancies.AsNoTracking() on p.PersonId equals s.PersonId into staffGroup
                        from s in staffGroup.DefaultIfEmpty()
                        join v in _db.Vacancies.AsNoTracking() on s.VacancyId equals v.VacancyId into vacGroup
                        from v in vacGroup.DefaultIfEmpty()
                        join j in _db.JobTitles.AsNoTracking() on v.JobTitleId equals j.Id into jobGroup
                        from j in jobGroup.DefaultIfEmpty()
                        where p.IsActive
                        select new
                        {
                            p.PersonId,
                            p.TenantId,
                            p.FullName,
                            p.PersonalEmail,
                            StaffId = s != null ? (Guid?)s.StaffId : null,
                            LoginId = s != null ? s.LoginId : null,
                            Department = v != null ? v.Department : null,
                            Designation = j != null ? j.TitleName : (v != null ? v.JobTitle : null)
                        };

            if (organizationId.HasValue && organizationId.Value > 0)
            {
                query = query.Where(x => x.TenantId == organizationId.Value);
            }

            if (!string.IsNullOrWhiteSpace(prefix))
            {
                var cleanPrefix = prefix.Trim();
                query = query.Where(x =>
                    x.FullName.Contains(cleanPrefix) ||
                    (x.LoginId != null && x.LoginId.Contains(cleanPrefix)) ||
                    (x.PersonalEmail != null && x.PersonalEmail.Contains(cleanPrefix)));
            }

            var results = await query.Take(25).ToListAsync(ct);

            return results.Select(x => new StaffEmailSuggestionDto(
                x.PersonId,
                x.StaffId,
                x.FullName,
                x.LoginId ?? x.PersonalEmail ?? x.FullName,
                x.PersonalEmail,
                x.Department,
                x.Designation
            )).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search email suggestions with prefix {Prefix}", prefix);
            throw;
        }
    }

    public async Task<tblMessage?> GetEmailDetailsAsync(int id, string user, CancellationToken ct = default)
    {
        try
        {
            var message = await _db.tblMessage.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (message == null) return null;

            var attachments = await _db.tblEmailDocuments.AsNoTracking()
                .Where(x => x.EmailId == id)
                .ToListAsync(ct);

            message.Attachments.AddRange(attachments);

            // Auto-mark as read if current user is receiver
            var handles = await ResolveUserHandlesAsync(user, ct);
            if (message.ReceiverEmail != null && handles.Contains(message.ReceiverEmail) && message.isRead != true)
            {
                await OpenedEmailAsync(id, user, ct);
                message.isRead = true;
            }

            return message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get email details for {Id}", id);
            throw;
        }
    }

    private async Task<HashSet<string>> ResolveUserHandlesAsync(string user, CancellationToken ct)
    {
        var handles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(user))
        {
            handles.Add(user);
        }

        var person = await _db.Persons
            .Include(p => p.Staff)
            .AsNoTracking()
            .FirstOrDefaultAsync(p =>
                p.IdentityUserId == user ||
                (p.Staff != null && p.Staff.LoginId == user) ||
                p.PersonalEmail == user, ct);

        if (person != null)
        {
            if (!string.IsNullOrWhiteSpace(person.Staff?.LoginId))
                handles.Add(person.Staff.LoginId);

            if (!string.IsNullOrWhiteSpace(person.PersonalEmail))
                handles.Add(person.PersonalEmail);
        }

        return handles;
    }

    private async Task<string> ResolvePrimarySenderHandleAsync(string user, CancellationToken ct)
    {
        var person = await _db.Persons
            .Include(p => p.Staff)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.IdentityUserId == user, ct);

        return person?.Staff?.LoginId ?? person?.PersonalEmail ?? user;
    }

    private string EnsureUploadDirectory()
    {
        var root = _env.WebRootPath;
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
        }

        var uploads = Path.Combine(root, "EmailDocuments");
        if (!Directory.Exists(uploads))
        {
            Directory.CreateDirectory(uploads);
        }

        return uploads;
    }
}
