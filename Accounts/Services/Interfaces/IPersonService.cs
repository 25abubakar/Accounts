using Accounts.Models;
using static Accounts.Controllers.PersonsController;

namespace Accounts.Services.Interfaces
{
    public interface IPersonService
    {
        Task<IEnumerable<PersonDto>> GetAllAsync();
        Task<IEnumerable<PersonDto>> GetUnassignedAsync();
        Task<PersonDto?> GetByIdAsync(Guid id);
        Task<IEnumerable<PersonProfileDto>> GetProfilesAsync();
        Task<PersonProfileDto?> GetProfileAsync(Guid id);
        Task<object> GetOrgTreeAsync();
        Task<object?> PreviewLoginIdAsync(int branchId);
        Task<(PersonDto? Person, string? GeneratedLoginId, string? GeneratedPassword, string? Error, int StatusCode)> RegisterAsync(RegisterPersonDto dto);
        Task<(PersonDto? Person, string? Error)> UpdateAsync(Guid id, UpdatePersonDto dto);
        Task<(string? PhotoUrl, string? FullUrl, string? Error)> UploadPhotoAsync(Guid id, IFormFile photo, string baseUrl);
        Task<(bool Success, string Message)> DeleteAsync(Guid id);

        // ── Password Management ───────────────────────────────────────────────

        /// <summary>Employee changes their own password (requires current password)</summary>
        Task<(bool Success, string Message)> ChangePasswordAsync(Guid personId, string currentPassword, string newPassword);

        /// <summary>Admin resets password for any person — no current password needed</summary>
        Task<(bool Success, string Message, string? NewPassword)> ResetPasswordAsync(Guid personId, string? newPassword = null);

        /// <summary>Reset password back to the default (LoginId@)</summary>
        Task<(bool Success, string Message, string? DefaultPassword)> ResetToDefaultPasswordAsync(Guid personId);

        /// <summary>Preview the email that will be auto-generated for a given name + branch</summary>
        Task<object?> PreviewEmailAsync(int branchId, string fullName);
    }
}
