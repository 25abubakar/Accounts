using Accounts.Models;

namespace Accounts.Services.Interfaces
{
    public interface IVacancyService
    {
        Task<IEnumerable<VacancyDto>> GetAllAsync();
        Task<VacancyDto?> GetByIdAsync(Guid id);
        Task<IEnumerable<VacancyDto>> GetVacantAsync();
        Task<IEnumerable<VacancyDto>> GetFilledAsync();
        Task<IEnumerable<VacancyDto>> GetByNodeAsync(int orgId);
        Task<IEnumerable<OrgVacancyReportDto>> GetReportAsync();
        Task<string?> PreviewCodeAsync(int organizationId, string jobTitle);
        Task<(VacancyDto? Vacancy, string? Error)> CreateAsync(CreateVacancyDto dto);
        Task<(IEnumerable<VacancyDto> Created, IEnumerable<string> Errors)> CreateBulkAsync(CreateVacancyDto dto);
        Task<(VacancyDto? Vacancy, string? Error)> UpdateAsync(Guid id, UpdateVacancyDto dto);
        Task<(bool Success, string Message)> DeleteAsync(Guid id);
    }
}
