using Accounts.Data;
using Accounts.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

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

        public DataAccessController(IPermissionFilterService filterService, ApplicationDbContext db)
        {
            _filterService = filterService;
            _db = db;
        }

        private async Task<(bool Success, Guid? StaffId, string Message)> GetCurrentStaffIdAsync()
        {
            // SuperAdmin bypass
            if (User.IsInRole("SuperAdmin"))
            {
                return (false, null, "SuperAdmin has access to all data. Use specific endpoints instead.");
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
    }
}
