using Accounts.Data;
using Accounts.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Data;
using Microsoft.Data.SqlClient;
using System.Collections.Generic;

namespace Accounts.Controllers
{
    /// <summary>
    /// Controller for accessing data based on user permissions.
    /// Only returns data the logged-in user has permission to view.
    /// </summary>
    [ApiController]
    [Route("api/data")]
    [Authorize]
    [Produces("application/json")]
    public class DataAccessController : ControllerBase
    {
        private readonly IPermissionFilterService _filterService;
        private readonly ApplicationDbContext _db;
        private readonly IOrganizationEmployeeQueryService _orgEmployeeQuery;

        public DataAccessController(
            IPermissionFilterService filterService,
            ApplicationDbContext db,
            IOrganizationEmployeeQueryService orgEmployeeQuery)
        {
            _filterService = filterService;
            _db = db;
            _orgEmployeeQuery = orgEmployeeQuery;
        }

        private async Task<(bool Success, Guid? StaffId, string Message)> GetCurrentStaffIdAsync()
        {
            // SuperAdmin / Admin bypass
            if (User.IsInRole("SuperAdmin") || User.IsInRole("Admin"))
            {
                return (true, Guid.Empty, "Full admin access.");
            }

            var identityUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(identityUserId))
            {
                return (false, null, "Cannot resolve user identity.");
            }

            var person = await _db.Persons
                .AsNoTracking()
                .Include(p => p.Staff)
                .FirstOrDefaultAsync(p => p.IdentityUserId == identityUserId);

            if (person == null)
            {
                return (false, null, "No person record found for this user.");
            }

            if (person.Staff == null)
            {
                return (false, null, "User is not assigned to a staff position.");
            }

            return (true, person.Staff.StaffId, "Success");
        }

        /// <summary>
        /// Get all data the current user has permission to access.
        /// Returns filtered departments, staff, persons, vacancies, and groups.
        /// </summary>
        [HttpGet("accessible")]
        public async Task<IActionResult> GetAccessibleData()
        {
            var (success, staffId, message) = await GetCurrentStaffIdAsync();
            if (!success)
            {
                return Unauthorized(new { message });
            }

            var data = await _filterService.GetAccessibleDataAsync(staffId!.Value);
            return Ok(data);
        }

        /// <summary>
        /// Get all features/permissions the current user has access to.
        /// </summary>
        [HttpGet("my-permissions")]
        public async Task<IActionResult> GetMyPermissions()
        {
            var (success, staffId, message) = await GetCurrentStaffIdAsync();
            if (!success)
            {
                return Unauthorized(new { message });
            }

            var permissions = await _filterService.GetAccessibleFeaturesAsync(staffId!.Value);
            return Ok(new
            {
                staffId = staffId.Value,
                permissions = permissions.OrderBy(p => p).ToList(),
                totalCount = permissions.Count()
            });
        }

        /// <summary>
        /// Check if current user has access to a specific feature.
        /// </summary>
        [HttpGet("can-access/{featureKey}")]
        public async Task<IActionResult> CanAccessFeature(string featureKey)
        {
            var (success, staffId, message) = await GetCurrentStaffIdAsync();
            if (!success)
            {
                return Unauthorized(new { message });
            }

            var hasAccess = await _filterService.CanAccessFeatureAsync(staffId!.Value, featureKey);
            return Ok(new
            {
                staffId = staffId.Value,
                featureKey,
                hasAccess
            });
        }

        /// <summary>
        /// Get departments the current user can view.
        /// </summary>
        [HttpGet("departments")]
        public async Task<IActionResult> GetAccessibleDepartments()
        {
            var (success, staffId, message) = await GetCurrentStaffIdAsync();
            if (!success)
            {
                return Unauthorized(new { message });
            }

            var departments = await _filterService.GetAccessibleDepartmentsAsync(staffId!.Value);
            return Ok(departments);
        }

        /// <summary>
        /// Get staff members the current user can view.
        /// </summary>
        [HttpGet("staff")]
        public async Task<IActionResult> GetAccessibleStaff()
        {
            var (success, staffId, message) = await GetCurrentStaffIdAsync();
            if (!success)
            {
                return Unauthorized(new { message });
            }

            var staff = await _filterService.GetAccessibleStaffAsync(staffId!.Value);
            return Ok(staff);
        }

        /// <summary>
        /// Get persons the current user can view.
        /// </summary>
        [HttpGet("persons")]
        public async Task<IActionResult> GetAccessiblePersons()
        {
            var (success, staffId, message) = await GetCurrentStaffIdAsync();
            if (!success)
            {
                return Unauthorized(new { message });
            }

            var persons = await _filterService.GetAccessiblePersonsAsync(staffId!.Value);
            return Ok(persons);
        }

        /// <summary>
        /// Recursive org query (Country/Company/Branch/Dept subtree) with full detail.
        /// Calls dbo.usp_GetEmployeesByOrgNode.
        /// </summary>
        [HttpGet("org/{orgNodeId:int}/employees")]
        public async Task<IActionResult> GetEmployeesByOrgNode(int orgNodeId)
        {
            if (orgNodeId <= 0)
                return BadRequest(new { message = "orgNodeId is required." });

            var rows = new List<Dictionary<string, object?>>();

            await using var conn = _db.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "dbo.usp_GetEmployeesByOrgNode";
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.Parameters.Add(new SqlParameter("@OrgNodeId", SqlDbType.Int) { Value = orgNodeId });

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    var value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    row[reader.GetName(i)] = value;
                }
                rows.Add(row);
            }

            return Ok(rows);
        }

        /// <summary>
        /// Clean vacancy/person rows for org subtree. Optional jobTitle or role filter (filled only when filtered).
        /// </summary>
        [HttpGet("org/{orgNodeId:int}/vacancy-persons")]
        public async Task<IActionResult> GetVacancyPersonsByOrgNode(
            int orgNodeId,
            [FromQuery] string? jobTitle,
            [FromQuery(Name = "role")] string? role,
            CancellationToken cancellationToken)
        {
            if (orgNodeId <= 0)
                return BadRequest(new { message = "orgNodeId is required." });

            var filter = !string.IsNullOrWhiteSpace(jobTitle) ? jobTitle : role;
            if (!string.IsNullOrWhiteSpace(filter))
            {
                var rows = await _orgEmployeeQuery.GetEmployeesByOrgAndRoleAsync(
                    orgNodeId, filter, cancellationToken);
                return Ok(rows);
            }

            var vacancyPersons = await _orgEmployeeQuery.GetPersonsByOrgNodeCleanAsync(
                orgNodeId, cancellationToken);
            return Ok(vacancyPersons);
        }
    }
}
