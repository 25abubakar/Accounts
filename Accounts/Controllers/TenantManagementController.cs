using Accounts.Data;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Accounts.Controllers
{
    [ApiController]
    [Route("api/tenant-management")]
    [Authorize]
    public sealed class TenantManagementController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _users;

        public TenantManagementController(ApplicationDbContext db, UserManager<ApplicationUser> users)
        {
            _db = db;
            _users = users;
        }

        [HttpGet("scope")]
        public async Task<IActionResult> GetScope(CancellationToken ct)
        {
            var caller = await GetCallerAsync();
            if (caller == null || (!caller.IsSuperAdmin && !caller.IsTenantAdmin && !User.IsInRole("CEO"))) return Forbid();
            if (caller.IsSuperAdmin) return Ok(new { scopeType = "SuperAdmin", items = Array.Empty<object>() });
            if (!await HasTenantManagementMenuAsync(caller.TenantId!.Value, ct)) return Forbid();

            var tenant = await _db.Tenants.IgnoreQueryFilters().AsNoTracking()
                .Include(t => t.OrganizationNode)
                .SingleAsync(t => t.Id == caller.TenantId.Value, ct);
            var allNodes = await _db.OrganizationTree.AsNoTracking().ToListAsync(ct);
            var nodeById = allNodes.ToDictionary(n => n.Id);
            var label = tenant.OrganizationNode?.Label ?? "Company";

            if (label.Equals("Group", StringComparison.OrdinalIgnoreCase))
            {
                var companyNodeIds = allNodes.Where(n => n.Label.Equals("Company", StringComparison.OrdinalIgnoreCase)
                    && IsDescendant(n.Id, tenant.OrganizationTreeId, nodeById)).Select(n => n.Id).ToHashSet();
                var companies = await _db.Tenants.IgnoreQueryFilters().AsNoTracking()
                    .Where(t => companyNodeIds.Contains(t.OrganizationTreeId))
                    .Select(t => new { id = t.Id.ToString(), kind = "Company", name = t.TenantName,
                        code = t.TenantCode, t.IsActive, organizationTreeId = t.OrganizationTreeId })
                    .OrderBy(x => x.name).ToListAsync(ct);
                return Ok(new { scopeType = "Group", scopeName = tenant.TenantName, items = companies });
            }

            var departmentIds = allNodes.Where(n => n.Label.Equals("Department", StringComparison.OrdinalIgnoreCase)
                && IsDescendant(n.Id, tenant.OrganizationTreeId, nodeById)).Select(n => n.Id).ToHashSet();
            var departments = allNodes.Where(n => departmentIds.Contains(n.Id)).Select(n => new
            {
                id = n.Id.ToString(), kind = "Department", name = n.Name, code = n.Code,
                n.IsActive, organizationTreeId = n.Id
            });
            var staff = await _db.Persons.AsNoTracking()
                .Where(p => p.TenantId == tenant.Id)
                .Select(p => new
                {
                    id = p.PersonId.ToString(), kind = "Staff", name = p.FullName,
                    code = p.Staff != null ? p.Staff.LoginId : null, p.IsActive,
                    organizationTreeId = p.Staff != null && p.Staff.Vacancy != null
                        ? p.Staff.Vacancy.OrganizationId : 0,
                    departmentName = p.Staff != null && p.Staff.Vacancy != null
                        ? (p.Staff.Vacancy.Organization != null && p.Staff.Vacancy.Organization.Label == "Department"
                            ? p.Staff.Vacancy.Organization.Name : p.Staff.Vacancy.Department) : null,
                    jobTitle = p.Staff != null && p.Staff.Vacancy != null
                        ? (p.Staff.Vacancy.JobTitleNav != null ? p.Staff.Vacancy.JobTitleNav.TitleName : p.Staff.Vacancy.JobTitle)
                        : null
                }).OrderBy(x => x.name).ToListAsync(ct);
            return Ok(new { scopeType = "Company", scopeName = tenant.TenantName,
                items = departments.Cast<object>().Concat(staff.Cast<object>()) });
        }

        [HttpPut("items/{kind}/{id}/status")]
        public async Task<IActionResult> SetStatus(string kind, string id, [FromBody] ManagementStatusDto dto, CancellationToken ct)
        {
            var caller = await GetCallerAsync();
            if ((caller?.IsTenantAdmin != true && !User.IsInRole("CEO")) || caller?.TenantId == null) return Forbid();
            if (!await HasTenantManagementMenuAsync(caller.TenantId.Value, ct)) return Forbid();
            var ownTenant = await _db.Tenants.IgnoreQueryFilters().Include(t => t.OrganizationNode)
                .SingleAsync(t => t.Id == caller.TenantId.Value, ct);
            var nodes = await _db.OrganizationTree.ToListAsync(ct);
            var byId = nodes.ToDictionary(n => n.Id);

            if (kind.Equals("company", StringComparison.OrdinalIgnoreCase)
                && ownTenant.OrganizationNode?.Label.Equals("Group", StringComparison.OrdinalIgnoreCase) == true
                && int.TryParse(id, out var companyTenantId))
            {
                var company = await _db.Tenants.IgnoreQueryFilters().Include(t => t.OrganizationNode)
                    .SingleOrDefaultAsync(t => t.Id == companyTenantId, ct);
                if (company == null || !IsDescendant(company.OrganizationTreeId, ownTenant.OrganizationTreeId, byId)) return Forbid();
                company.IsActive = dto.IsActive;
                if (company.OrganizationNode != null) company.OrganizationNode.IsActive = dto.IsActive;
                await _db.SaveChangesAsync(ct);
                return Ok(new { message = $"{company.TenantName} is now {(dto.IsActive ? "active" : "disabled")}." });
            }

            if (ownTenant.OrganizationNode?.Label.Equals("Company", StringComparison.OrdinalIgnoreCase) != true) return Forbid();
            if (kind.Equals("department", StringComparison.OrdinalIgnoreCase) && int.TryParse(id, out var departmentId))
            {
                if (!byId.TryGetValue(departmentId, out var department)
                    || !department.Label.Equals("Department", StringComparison.OrdinalIgnoreCase)
                    || !IsDescendant(departmentId, ownTenant.OrganizationTreeId, byId)) return Forbid();
                department.IsActive = dto.IsActive;
                await _db.SaveChangesAsync(ct);
                return Ok(new { message = $"{department.Name} and its assigned staff are now {(dto.IsActive ? "active" : "disabled")}." });
            }

            if (kind.Equals("staff", StringComparison.OrdinalIgnoreCase) && Guid.TryParse(id, out var personId))
            {
                var person = await _db.Persons.SingleOrDefaultAsync(p => p.PersonId == personId && p.TenantId == ownTenant.Id, ct);
                if (person == null) return NotFound(new { message = "Staff member not found." });
                person.IsActive = dto.IsActive;
                var identity = await _users.FindByIdAsync(person.IdentityUserId);
                if (identity != null)
                {
                    identity.LockoutEnabled = true;
                    identity.LockoutEnd = dto.IsActive ? null : DateTimeOffset.MaxValue;
                    await _users.UpdateAsync(identity);
                }
                await _db.SaveChangesAsync(ct);
                return Ok(new { message = $"{person.FullName} is now {(dto.IsActive ? "active" : "disabled")}." });
            }
            return BadRequest(new { message = "Unsupported management item." });
        }

        private async Task<ApplicationUser?> GetCallerAsync() =>
            await _users.FindByIdAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        private async Task<bool> HasTenantManagementMenuAsync(int tenantId, CancellationToken ct) =>
            await _db.TenantMenuPermissions.IgnoreQueryFilters().AnyAsync(p => p.TenantId == tenantId
                && p.IsAllow && p.CanView && p.Menu != null && p.Menu.Route == "/tenants", ct);

        private static bool IsDescendant(int candidate, int ancestor, IReadOnlyDictionary<int, OrganizationTree> nodes)
        {
            var visited = new HashSet<int>(); int? current = candidate;
            while (current.HasValue && nodes.TryGetValue(current.Value, out var node) && visited.Add(node.Id))
            { if (node.ParentId == ancestor) return true; current = node.ParentId; }
            return false;
        }
    }

    public sealed class ManagementStatusDto { public bool IsActive { get; set; } }
}
