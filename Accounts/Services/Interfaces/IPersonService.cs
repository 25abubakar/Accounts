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
        Task<(PersonDto? Person, string? Error, int StatusCode)> RegisterAsync(RegisterPersonDto dto);
        Task<(PersonDto? Person, string? Error)> UpdateAsync(Guid id, UpdatePersonDto dto);
        Task<(string? PhotoUrl, string? FullUrl, string? Error)> UploadPhotoAsync(Guid id, IFormFile photo, string baseUrl);
        Task<(bool Success, string Message)> DeleteAsync(Guid id);
    }
}
