using Accounts.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Accounts.Controllers
{

    [ApiController]
    [Route("api/organization")]
    [Authorize]
    [Produces("application/json")]
    public class OrganizationEmployeesController : ControllerBase
    {
        private readonly IOrganizationEmployeeQueryService _query;

        public OrganizationEmployeesController(IOrganizationEmployeeQueryService query) =>
            _query = query;

        [HttpGet("{orgId:int}/vacancy-persons")]
        public async Task<IActionResult> GetVacancyPersons(int orgId, CancellationToken cancellationToken)
        {
            if (orgId <= 0)
                return BadRequest(new { message = "orgId must be a positive integer." });

            var rows = await _query.GetPersonsByOrgNodeCleanAsync(orgId, cancellationToken);
            return Ok(rows);
        }

        [HttpGet("{orgId:int}/employees-by-role")]
        public async Task<IActionResult> GetEmployeesByRole(
            int orgId,
            [FromQuery] string? jobTitle,
            [FromQuery(Name = "role")] string? role,
            CancellationToken cancellationToken)
        {
            if (orgId <= 0)
                return BadRequest(new { message = "orgId must be a positive integer." });

            var filter = !string.IsNullOrWhiteSpace(jobTitle) ? jobTitle : role;
            var rows = await _query.GetEmployeesByOrgAndRoleAsync(orgId, filter, cancellationToken);
            return Ok(rows);
        }
    }
}
