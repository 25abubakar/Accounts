using Accounts.Data;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace Accounts.Services.Services
{
    public class OrganizationEmployeeQueryService : IOrganizationEmployeeQueryService
    {
        private readonly ApplicationDbContext _db;

        public OrganizationEmployeeQueryService(ApplicationDbContext db) => _db = db;

        public async Task<IReadOnlyList<OrganizationVacancyPersonDto>> GetPersonsByOrgNodeCleanAsync(
            int orgNodeId,
            CancellationToken cancellationToken = default)
        {
            var orgParam = new SqlParameter("@OrgNodeId", SqlDbType.Int) { Value = orgNodeId };

            return await _db.Set<OrganizationVacancyPersonDto>()
                .FromSqlRaw("EXEC [dbo].[usp_GetPersonsByOrgNode_Clean] @OrgNodeId", orgParam)
                .AsNoTracking()
                .ToListAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<EmployeeByOrgAndRoleDto>> GetEmployeesByOrgAndRoleAsync(
            int orgNodeId,
            string? jobTitle,
            CancellationToken cancellationToken = default)
        {
            var orgParam = new SqlParameter("@OrgNodeId", SqlDbType.Int) { Value = orgNodeId };
            var jobParam = new SqlParameter("@JobTitle", SqlDbType.NVarChar, 100)
            {
                Value = string.IsNullOrWhiteSpace(jobTitle) ? DBNull.Value : jobTitle.Trim()
            };

            return await _db.Set<EmployeeByOrgAndRoleDto>()
                .FromSqlRaw(
                    "EXEC [dbo].[usp_GetEmployeesByOrgAndRole] @OrgNodeId, @JobTitle",
                    orgParam,
                    jobParam)
                .AsNoTracking()
                .ToListAsync(cancellationToken);
        }
    }
}
