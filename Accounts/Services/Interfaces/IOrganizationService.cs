using Accounts.Models;

namespace Accounts.Services.Interfaces
{
    public interface IOrganizationService
    {
        // Lookup
        Task<CountryLookupDto?> CountryLookupAsync(string name);
        Task<IEnumerable<CountryLookupDto>> CountrySearchAsync(string q);

        // Tree
        Task<IEnumerable<OrgTreeNodeDto>> GetTreeAsync();
        Task<IEnumerable<OrgTreeNodeDto>?> GetSubTreeAsync(int startId);
        Task<IEnumerable<OrgFlatTreeDto>> GetFlatTreeAsync();

        // CRUD
        Task<IEnumerable<OrgNodeDto>> GetAllAsync();
        Task<OrgNodeDto?> GetByIdAsync(int id);
        Task<IEnumerable<OrgNodeDto>> GetByLabelAsync(string label);
        Task<IEnumerable<OrgNodeDto>?> GetChildrenAsync(int id);
        Task<IEnumerable<OrgNodeDto>> SearchAsync(string q);
        Task<(OrgNodeDto Node, bool Created)> CreateAsync(CreateOrgNodeDto dto);
        Task<OrgNodeDto?> UpdateAsync(int id, UpdateOrgNodeDto dto);
        Task<(bool Success, string Message)> DeleteAsync(int id);
    }
}
