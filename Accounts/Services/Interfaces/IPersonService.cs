using Accounts.DTOs;

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
        Task<object?> PreviewEmailAsync(int branchId, string fullName);
        Task<(PersonDto? Person, string? GeneratedLoginId, string? GeneratedPassword, string? Error, int StatusCode)> RegisterAsync(RegisterPersonDto dto);
        Task<(PersonDto? Person, string? Error)> UpdateAsync(Guid id, UpdatePersonDto dto);
        Task<(string? PhotoUrl, string? FullUrl, string? Error)> UploadPhotoAsync(Guid id, IFormFile photo, string baseUrl);
        Task<(bool Success, string Message)> DeleteAsync(Guid id);
        Task<(bool Success, string Message)> ChangePasswordAsync(Guid personId, string currentPassword, string newPassword);
        Task<(bool Success, string Message, string? NewPassword)> ResetPasswordAsync(Guid personId, string? newPassword = null);
        Task<(bool Success, string Message, string? DefaultPassword)> ResetToDefaultPasswordAsync(Guid personId);
    }
}
