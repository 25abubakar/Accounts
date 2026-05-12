using Accounts.Models;

namespace Accounts.Services.Interfaces
{
    public interface IStaffService
    {
        Task<IEnumerable<StaffDto>> GetAllAsync();
        Task<StaffDto?> GetByIdAsync(Guid id);
        Task<IEnumerable<StaffDto>> SearchAsync(string q);
        Task<(StaffDto? Staff, string? Error)> HireAsync(Guid vacancyId, HireStaffDto dto);
        Task<(StaffDto? Staff, string? Error)> HirePersonAsync(Guid vacancyId, Guid personId);
        Task<(StaffDto? Staff, string? Error)> UpdateAsync(Guid id, UpdateStaffDto dto);
        Task<(StaffDto? Staff, string? Error)> TransferAsync(Guid id, TransferStaffDto dto);
        Task<(bool Success, string Message)> DeleteAsync(Guid id);
        Task<(string? PhotoUrl, string? FullUrl, string? Error)> UploadPhotoAsync(Guid id, IFormFile photo, string baseUrl);
        Task<(bool Success, string Message)> DeletePhotoAsync(Guid id);
    }
}
