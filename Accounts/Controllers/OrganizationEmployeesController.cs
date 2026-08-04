using Accounts.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Accounts.Controllers
{
    /// <summary>
    /// Hierarchical employee / vacancy data for the HR org tree (stored procedures).
    /// </summary>
    [ApiController]
    [Route("api/organization")]
    [Authorize]
    [Produces("application/json")]
    public class OrganizationEmployeesController : ControllerBase
    {
        private readonly IOrganizationEmployeeQueryService _query;
        private readonly ITenantService _tenantService;
        private readonly IOrganizationScopeService _organizationScope;

        public OrganizationEmployeesController(
            IOrganizationEmployeeQueryService query,
            ITenantService tenantService,
            IOrganizationScopeService organizationScope)
        {
            _query = query;
            _tenantService = tenantService;
            _organizationScope = organizationScope;
        }

        /// <summary>
        /// Org subtree with vacancies and assigned persons (includes unfilled seats).
        /// Calls dbo.usp_GetPersonsByOrgNode_Clean.
        /// </summary>
        [HttpGet("{orgId:int}/vacancy-persons")]
        public async Task<IActionResult> GetVacancyPersons(int orgId, CancellationToken cancellationToken)
        {
            if (orgId <= 0)
                return BadRequest(new { message = "orgId must be a positive integer." });
            if (_tenantService.IsSuperAdmin || !_tenantService.TenantId.HasValue)
                return Forbid();
            if (!await _organizationScope.IsWithinTenantSubtreeAsync(
                    _tenantService.TenantId.Value,
                    orgId,
                    cancellationToken))
                return Forbid();

            var rows = await _query.GetPersonsByOrgNodeCleanAsync(
                _tenantService.TenantId.Value,
                orgId,
                cancellationToken);
            return Ok(rows);
        }

        /// <summary>
        /// Filled positions in org subtree. Optional jobTitle or role query filter (exact match).
        /// Calls dbo.usp_GetEmployeesByOrgAndRole.
        /// </summary>
        [HttpGet("{orgId:int}/employees-by-role")]
        public async Task<IActionResult> GetEmployeesByRole(
            int orgId,
            [FromQuery] string? jobTitle,
            [FromQuery(Name = "role")] string? role,
            CancellationToken cancellationToken)
        {
            if (orgId <= 0)
                return BadRequest(new { message = "orgId must be a positive integer." });
            if (_tenantService.IsSuperAdmin || !_tenantService.TenantId.HasValue)
                return Forbid();
            if (!await _organizationScope.IsWithinTenantSubtreeAsync(
                    _tenantService.TenantId.Value,
                    orgId,
                    cancellationToken))
                return Forbid();

            var filter = !string.IsNullOrWhiteSpace(jobTitle) ? jobTitle : role;
            var rows = await _query.GetEmployeesByOrgAndRoleAsync(
                _tenantService.TenantId.Value,
                orgId,
                filter,
                cancellationToken);
            return Ok(rows);
        }
    }
}
