using Accounts.Models;
using Accounts.Data;
using Accounts.Services.Interfaces;
using Accounts.Services.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Accounts.Controllers
{
    /// <summary>
    /// Positions/Vacancies API — accessible to Tenant Admins and Staff.
    /// Super Admin is blocked (no operational data access).
    /// Data is automatically scoped per tenant via EF Core Global Query Filters.
    /// </summary>
    [ApiController]
    [Route("api/positions")]
    [Authorize]
    [Produces("application/json")]
    public class VacanciesController : ControllerBase
    {
        private readonly IVacancyService              _service;
        private readonly ApplicationDbContext         _db;
        private readonly RbacService                  _rbac;
        private readonly ITenantService               _tenant;

        public VacanciesController(IVacancyService service, ApplicationDbContext db, RbacService rbac, ITenantService tenant)
        {
            _service     = service;
            _db          = db;
            _rbac        = rbac;
            _tenant      = tenant;
        }

        private Task<bool> CallerIsSuperAdminAsync() => Task.FromResult(
            User.IsInRole("SuperAdmin") ||
            string.Equals(User.FindFirstValue(ITenantService.ClaimIsSuperAdmin), "true", StringComparison.OrdinalIgnoreCase));

        private Task<bool> CallerIsTenantAdminAsync() => Task.FromResult(
            User.IsInRole("TenantAdmin") ||
            string.Equals(User.FindFirstValue(ITenantService.ClaimIsTenantAdmin), "true", StringComparison.OrdinalIgnoreCase));

        private bool CallerHasFullTenantAccess() =>
            User.IsInRole("Admin") ||
            User.IsInRole("TenantAdmin") ||
            string.Equals(User.FindFirstValue(ITenantService.ClaimIsTenantAdmin), "true", StringComparison.OrdinalIgnoreCase);

        private async Task<bool> HasVacancyActionAsync(string action, params string[] semanticKeys)
        {
            if (CallerHasFullTenantAccess()) return true;

            var identityUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(identityUserId)) return false;

            var staffId = await _db.Persons.AsNoTracking()
                .Where(person => person.IdentityUserId == identityUserId && person.Staff != null)
                .Select(person => (Guid?)person.Staff!.StaffId)
                .FirstOrDefaultAsync();
            if (!staffId.HasValue) return false;

            var menuId = await _db.Menus.AsNoTracking()
                .Where(menu => menu.IsActive && (menu.Route == "/hr/vacancies" || menu.Route == "/positions"))
                .OrderBy(menu => menu.Route == "/hr/vacancies" ? 0 : 1)
                .Select(menu => (int?)menu.Id)
                .FirstOrDefaultAsync();

            var normalizedAction = action.Trim().ToUpperInvariant();
            if (menuId.HasValue)
            {
                if (normalizedAction == "VIEW" && await _rbac.HasAccessAsync(staffId.Value, $"MENU_{menuId.Value}"))
                    return true;
                if (await _rbac.HasAccessAsync(staffId.Value, $"MENU_{menuId.Value}_{normalizedAction}"))
                    return true;
            }

            foreach (var key in semanticKeys)
                if (await _rbac.HasAccessAsync(staffId.Value, key)) return true;

            return false;
        }

        private async Task<HashSet<int>?> GetVisibleOrganizationIdsAsync()
        {
            if (CallerHasFullTenantAccess()) return null;
            if (!_tenant.TenantId.HasValue) return [];

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var assignedNodeId = await _db.Persons.AsNoTracking()
                .Where(person => person.TenantId == _tenant.TenantId.Value
                    && person.IdentityUserId == userId && person.IsActive
                    && person.Staff != null && person.Staff.Vacancy != null)
                .Select(person => (int?)person.Staff!.Vacancy!.OrganizationId)
                .FirstOrDefaultAsync();
            if (!assignedNodeId.HasValue) return [];

            var tenantRoot = await _db.Tenants.AsNoTracking()
                .Where(tenant => tenant.Id == _tenant.TenantId.Value)
                .Select(tenant => tenant.OrganizationTreeId)
                .FirstAsync();
            var nodes = await _db.OrganizationTree.AsNoTracking()
                .Select(node => new { node.Id, node.ParentId, node.Label }).ToListAsync();
            var byId = nodes.ToDictionary(node => node.Id);
            var currentId = assignedNodeId.Value;
            var scopeRoot = assignedNodeId.Value;
            var insideTenant = false;
            var visited = new HashSet<int>();
            while (visited.Add(currentId) && byId.TryGetValue(currentId, out var current))
            {
                if (current.Id == tenantRoot) insideTenant = true;
                if (!current.ParentId.HasValue) break;
                currentId = current.ParentId.Value;
            }
            if (!insideTenant) return [];

            var visible = new HashSet<int> { scopeRoot };
            var queue = new Queue<int>(); queue.Enqueue(scopeRoot);
            while (queue.Count > 0)
            {
                var parent = queue.Dequeue();
                foreach (var child in nodes.Where(node => node.ParentId == parent))
                    if (visible.Add(child.Id)) queue.Enqueue(child.Id);
            }
            return visible;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            if (await CallerIsSuperAdminAsync()) return Forbid();
            if (!await HasVacancyActionAsync("VIEW", "VACANCY_VIEW")) return Forbid();
            var rows = await _service.GetAllAsync();
            var visible = await GetVisibleOrganizationIdsAsync();
            return Ok(visible == null ? rows : rows.Where(row => visible.Contains(row.OrganizationId)));
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            if (await CallerIsSuperAdminAsync()) return Forbid();
            if (!await HasVacancyActionAsync("VIEW", "VACANCY_VIEW")) return Forbid();
            var v = await _service.GetByIdAsync(id);
            var visible = await GetVisibleOrganizationIdsAsync();
            if (v != null && visible != null && !visible.Contains(v.OrganizationId)) return Forbid();
            return v == null ? NotFound(new { message = $"Position {id} not found." }) : Ok(v);
        }

        [HttpGet("vacant")]
        public async Task<IActionResult> GetVacant()
        {
            if (await CallerIsSuperAdminAsync()) return Forbid();
            if (!await HasVacancyActionAsync("VIEW", "VACANCY_VIEW")) return Forbid();
            var rows = await _service.GetVacantAsync();
            var visible = await GetVisibleOrganizationIdsAsync();
            return Ok(visible == null ? rows : rows.Where(row => visible.Contains(row.OrganizationId)));
        }

        [HttpGet("filled")]
        public async Task<IActionResult> GetFilled()
        {
            if (await CallerIsSuperAdminAsync()) return Forbid();
            if (!await HasVacancyActionAsync("VIEW", "VACANCY_VIEW")) return Forbid();
            var rows = await _service.GetFilledAsync();
            var visible = await GetVisibleOrganizationIdsAsync();
            return Ok(visible == null ? rows : rows.Where(row => visible.Contains(row.OrganizationId)));
        }

        [HttpGet("by-node/{orgId:int}")]
        public async Task<IActionResult> GetByNode(int orgId)
        {
            if (await CallerIsSuperAdminAsync()) return Forbid();
            if (!await HasVacancyActionAsync("VIEW", "VACANCY_VIEW")) return Forbid();
            var visible = await GetVisibleOrganizationIdsAsync();
            if (visible != null && !visible.Contains(orgId)) return Forbid();
            return Ok(await _service.GetByNodeAsync(orgId));
        }

        [HttpGet("report")]
        public async Task<IActionResult> GetReport()
        {
            if (await CallerIsSuperAdminAsync()) return Forbid();
            if (!await HasVacancyActionAsync("VIEW", "VACANCY_VIEW")) return Forbid();
            return Ok(await _service.GetReportAsync());
        }

        [HttpGet("preview-code")]
        public async Task<IActionResult> PreviewCode([FromQuery] int organizationId, [FromQuery] string jobTitle)
        {
            if (await CallerIsSuperAdminAsync()) return Forbid();
            if (!await HasVacancyActionAsync("ADD", "VACANCY_CREATE")) return Forbid();
            if (organizationId <= 0 || string.IsNullOrWhiteSpace(jobTitle))
                return BadRequest(new { message = "organizationId and jobTitle are required." });
            var code = await _service.PreviewCodeAsync(organizationId, jobTitle);
            return code == null
                ? BadRequest(new { message = $"Organization node {organizationId} not found." })
                : Ok(new { vacancyCode = code });
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateVacancyDto dto)
        {
            if (await CallerIsSuperAdminAsync()) return Forbid();
            if (!await HasVacancyActionAsync("ADD", "VACANCY_CREATE")) return Forbid();
            if (!ModelState.IsValid) return BadRequest(ModelState);
            if (await CallerIsTenantAdminAsync() && !dto.JobTitleId.HasValue)
                return BadRequest(new { message = "Tenant Admins must select an existing job title from the Job Titles catalog." });
            if (dto.VacancyCount <= 1)
            {
                var (vacancy, error) = await _service.CreateAsync(dto);
                if (error != null)
                    return error.Contains("not found") ? NotFound(new { message = error }) : BadRequest(new { message = error });
                return CreatedAtAction(nameof(GetById), new { id = vacancy!.VacancyId }, vacancy);
            }
            var (created, errors) = await _service.CreateBulkAsync(dto);
            var list = created.ToList();
            return Ok(new { requested = dto.VacancyCount, created = list.Count, failed = errors.Count(), vacancies = list, errors = errors.Any() ? errors : null });
        }

        [HttpPost("bulk")]
        public async Task<IActionResult> CreateBulk([FromBody] CreateVacancyDto dto)
        {
            if (await CallerIsSuperAdminAsync()) return Forbid();
            if (!await HasVacancyActionAsync("ADD", "VACANCY_CREATE")) return Forbid();
            if (!ModelState.IsValid) return BadRequest(ModelState);
            if (dto.VacancyCount < 1) return BadRequest(new { message = "VacancyCount must be at least 1." });
            var (created, errors) = await _service.CreateBulkAsync(dto);
            var list = created.ToList();
            return Ok(new { requested = dto.VacancyCount, created = list.Count, failed = errors.Count(), vacancies = list, errors = errors.Any() ? errors : null });
        }

        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdateVacancyDto dto)
        {
            if (await CallerIsSuperAdminAsync()) return Forbid();
            if (!await HasVacancyActionAsync("EDIT", "VACANCY_EDIT")) return Forbid();
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var (vacancy, error) = await _service.UpdateAsync(id, dto);
            if (error != null) return error.Contains("not found") ? NotFound(new { message = error }) : BadRequest(new { message = error });
            return Ok(vacancy);
        }

        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            if (await CallerIsSuperAdminAsync()) return Forbid();
            if (!await HasVacancyActionAsync("DELETE", "VACANCY_DELETE")) return Forbid();
            var (success, message) = await _service.DeleteAsync(id);
            if (!success) return message.Contains("not found") ? NotFound(new { message }) : BadRequest(new { message });
            return Ok(new { message });
        }
    }
}
