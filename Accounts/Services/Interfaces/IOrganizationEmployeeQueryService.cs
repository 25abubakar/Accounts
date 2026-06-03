using Accounts.Models;

namespace Accounts.Services.Interfaces
{
    public interface IOrganizationEmployeeQueryService
    {
        /// <summary>
        /// Vacancies and persons under an org subtree (recursive). Includes unfilled seats.
        /// </summary>
        Task<IReadOnlyList<OrganizationVacancyPersonDto>> GetPersonsByOrgNodeCleanAsync(
            int orgNodeId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Filled positions under an org subtree; optional exact job-title filter.
        /// </summary>
        Task<IReadOnlyList<EmployeeByOrgAndRoleDto>> GetEmployeesByOrgAndRoleAsync(
            int orgNodeId,
            string? jobTitle,
            CancellationToken cancellationToken = default);
    }
}
