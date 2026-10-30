using Accounts.DTOs;
using Accounts.Models;
using Microsoft.AspNetCore.Http;

namespace Accounts.Services.Interfaces;

public interface ILTEmailService
{
    Task<List<tblMessage>> GetLogInUserEmailsAsync(string user, int? organizationId, CancellationToken ct = default);
    Task<List<tblMessage>> GetLogInUserSentEmailsAsync(string user, int? organizationId, CancellationToken ct = default);
    Task<string> ComposeEmailAsync(tblMessage message, List<IFormFile>? attachments, string user, int? organizationId, CancellationToken ct = default);
    Task<string> OpenedEmailAsync(int id, string? user = null, CancellationToken ct = default);
    Task<List<StaffEmailSuggestionDto>> SearchEmailSuggestionsAsync(string prefix, int? organizationId, CancellationToken ct = default);
    Task<tblMessage?> GetEmailDetailsAsync(int id, string user, CancellationToken ct = default);
}
