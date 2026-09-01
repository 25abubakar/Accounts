using Accounts.Authorization;
using Accounts.Data;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Accounts.Services.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Accounts.Controllers
{
    [ApiController]
    [Route("api/organization")]
    [Authorize]
    [Produces("application/json")]
    public class OrganizationTreeController : ControllerBase
    {
        private readonly IOrganizationService         _service;
        private readonly ApplicationDbContext         _db;
        private readonly ITenantService               _tenantService;
        private readonly RbacService                  _rbac;
        private readonly TenantPermissionService      _tenantPermissions;

        public OrganizationTreeController(
            IOrganizationService          service,
            ApplicationDbContext          db,
            ITenantService                tenantService,
            RbacService                   rbac,
            TenantPermissionService       tenantPermissions)
        {
            _service     = service;
            _db          = db;
            _tenantService = tenantService;
            _rbac        = rbac;
            _tenantPermissions = tenantPermissions;
        }

        private async Task<(bool isSuperAdmin, bool isTenantAdmin, int? tenantRootNodeId)>
            ResolveCallerContextAsync()
        {
            var isSuperAdmin = _tenantService.IsSuperAdmin;
            var isOrganizationAdmin = _tenantService.IsTenantAdmin;

            int? rootNodeId = null;

            if (!isSuperAdmin && _tenantService.TenantId.HasValue)
            {
                rootNodeId = await _db.Tenants.AsNoTracking()
                    .Where(tenant => tenant.Id == _tenantService.TenantId.Value)
                    .Select(tenant => (int?)tenant.OrganizationTreeId)
                    .FirstOrDefaultAsync();
            }

            return (isSuperAdmin, isOrganizationAdmin, rootNodeId);
        }

        private static readonly string[] InfrastructureLabels = ["Country", "Group", "Company"];

        private static bool IsInfrastructureLabel(string? label) =>
            !string.IsNullOrWhiteSpace(label) &&
            InfrastructureLabels.Contains(label, StringComparer.OrdinalIgnoreCase);

        private async Task<bool> HasOrganizationActionAsync(string action)
        {
            if (TenantPermissionService.IsSuperAdmin(User)) return true;
            if (TenantPermissionService.IsTenantAdmin(User))
                return await _tenantPermissions.HasMenuRouteAsync(
                    User,
                    ["/groups/companies", "/organization", "/groups/hierarchy"],
                    action);

            var identityUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(identityUserId)) return false;

            var staffId = await _db.Persons.AsNoTracking()
                .Where(person => person.IdentityUserId == identityUserId && person.Staff != null)
                .Select(person => (Guid?)person.Staff!.StaffId)
                .FirstOrDefaultAsync();
            if (!staffId.HasValue) return false;

            var normalizedAction = action.Trim().ToUpperInvariant();
            var menuIds = await _db.Menus.AsNoTracking()
                .Where(menu => menu.IsActive &&
                    (menu.Route == "/groups/companies" ||
                     menu.Route == "/organization" ||
                     menu.Route == "/groups/hierarchy"))
                .Select(menu => menu.Id)
                .ToListAsync();

            foreach (var menuId in menuIds)
            {
                if (normalizedAction == "VIEW" && await _rbac.HasAccessAsync(staffId.Value, $"MENU_{menuId}"))
                    return true;
                if (await _rbac.HasAccessAsync(staffId.Value, $"MENU_{menuId}_{normalizedAction}"))
                    return true;
            }

            return false;
        }

        [HttpGet("country-lookup")]
        public async Task<IActionResult> CountryLookup([FromQuery] string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return BadRequest(new { message = "Country name is required." });
            var result = await _service.CountryLookupAsync(name);
            return result == null
                ? NotFound(new { message = $"Country '{name}' not found." })
                : Ok(result);
        }

        [HttpGet("country-search")]
        public async Task<IActionResult> CountrySearch([FromQuery] string q)
        {
            if (string.IsNullOrWhiteSpace(q))
                return BadRequest(new { message = "Query 'q' is required." });
            return Ok(await _service.CountrySearchAsync(q));
        }

        [HttpGet("tree")]
        public async Task<IActionResult> GetTree()
        {
            var (isSuperAdmin, isTenantAdmin, tenantRootId) = await ResolveCallerContextAsync();
            var tree = await _service.GetTreeAsync();

            if (isSuperAdmin)
                return Ok(PruneTreeToCompanyLevel(tree));

            if (tenantRootId.HasValue)
            {
                var subtree = await _service.GetSubTreeAsync(tenantRootId.Value);
                return Ok(subtree ?? Enumerable.Empty<OrgTreeNodeDto>());
            }

            return Ok(tree);
        }

        [HttpGet("tree/{startId:int}")]
        public async Task<IActionResult> GetSubTree(int startId)
        {
            var result = await _service.GetSubTreeAsync(startId);
            return result == null
                ? NotFound(new { message = $"Node {startId} not found." })
                : Ok(result);
        }

        [HttpGet("flat-tree")]
        public async Task<IActionResult> GetFlatTree()
        {
            var (isSuperAdmin, isTenantAdmin, tenantRootId) = await ResolveCallerContextAsync();
            var flat = (await _service.GetFlatTreeAsync()).ToList();

            if (isSuperAdmin)
            {
                var topLabels = new[] { "Country", "Group", "Company" };
                return Ok(flat.Where(n =>
                    topLabels.Contains(n.Label, StringComparer.OrdinalIgnoreCase)));
            }

            if (tenantRootId.HasValue)
            {
                var subtreeIds = CollectSubtreeIds(flat, tenantRootId.Value);
                var nodeMap = flat.ToDictionary(n => n.Id);
                var extendedIds = new HashSet<int>(subtreeIds);
                foreach (var id in subtreeIds.ToList())
                {
                    var current = nodeMap.GetValueOrDefault(id);
                    while (current?.ParentId != null && nodeMap.TryGetValue(current.ParentId.Value, out var parent))
                    {
                        extendedIds.Add(parent.Id);
                        current = parent;
                    }
                }
                return Ok(flat.Where(n => extendedIds.Contains(n.Id)));
            }

            return Ok(flat);
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var (isSuperAdmin, isTenantAdmin, tenantRootId) = await ResolveCallerContextAsync();

            if (isTenantAdmin && tenantRootId.HasValue || (!isSuperAdmin && tenantRootId.HasValue))
            {
                var flat = (await _service.GetFlatTreeAsync()).ToList();
                var subtreeIds = CollectSubtreeIds(flat, tenantRootId!.Value);
                var nodeMap = flat.ToDictionary(n => n.Id);
                var extendedIds = new HashSet<int>(subtreeIds);
                foreach (var id in subtreeIds.ToList())
                {
                    var current = nodeMap.GetValueOrDefault(id);
                    while (current?.ParentId != null && nodeMap.TryGetValue(current.ParentId.Value, out var parent))
                    {
                        extendedIds.Add(parent.Id);
                        current = parent;
                    }
                }
                var all = await _service.GetAllAsync();
                return Ok(all.Where(n => extendedIds.Contains(n.Id)));
            }

            return Ok(await _service.GetAllAsync());
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id)
        {
            var node = await _service.GetByIdAsync(id);
            return node == null
                ? NotFound(new { message = $"Node {id} not found." })
                : Ok(node);
        }

        [HttpGet("by-label/{label}")]
        public async Task<IActionResult> GetByLabel(string label) =>
            Ok(await _service.GetByLabelAsync(label));

        [HttpGet("{id:int}/children")]
        public async Task<IActionResult> GetChildren(int id)
        {
            var children = await _service.GetChildrenAsync(id);
            return children == null
                ? NotFound(new { message = $"Node {id} not found." })
                : Ok(children);
        }

        [HttpGet("search")]
        public async Task<IActionResult> Search([FromQuery] string q)
        {
            if (string.IsNullOrWhiteSpace(q))
                return BadRequest(new { message = "Query 'q' is required." });
            return Ok(await _service.SearchAsync(q));
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateOrgNodeDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            if (!await HasOrganizationActionAsync("ADD")) return Forbid();

            var (isSuperAdmin, isTenantAdmin, tenantRootId) = await ResolveCallerContextAsync();

            if (!isSuperAdmin)
            {
                if (IsInfrastructureLabel(dto.Label))
                    return BadRequest(new { message = "Only Super Admin can create Country, Group, or Company nodes." });

                if (dto.ParentId.HasValue && tenantRootId.HasValue)
                {
                    var flat = (await _service.GetFlatTreeAsync()).ToList();
                    var allowed = CollectSubtreeIds(flat, tenantRootId.Value);
                    if (!allowed.Contains(dto.ParentId.Value))
                        return Forbid();
                }
            }

            if (dto.ParentId.HasValue)
            {
                var parent = await _service.GetByIdAsync(dto.ParentId.Value);
                if (parent == null)
                    return BadRequest(new { message = $"Parent node {dto.ParentId} does not exist." });
            }

            var (node, _) = await _service.CreateAsync(dto);
            return CreatedAtAction(nameof(GetById), new { id = node.Id }, node);
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] UpdateOrgNodeDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            if (!await HasOrganizationActionAsync("EDIT")) return Forbid();

            var (isSuperAdmin, _, tenantRootId) = await ResolveCallerContextAsync();
            var existing = await _service.GetByIdAsync(id);
            if (existing == null)
                return NotFound(new { message = $"Node {id} not found." });

            if (!isSuperAdmin)
            {
                if (IsInfrastructureLabel(existing.Label))
                    return BadRequest(new { message = "Only Super Admin can edit Country, Group, or Company nodes." });

                if (IsInfrastructureLabel(dto.Label))
                    return BadRequest(new { message = "Only Super Admin can assign Country, Group, or Company labels." });

                if (dto.ParentId.HasValue && tenantRootId.HasValue)
                {
                    var flat = (await _service.GetFlatTreeAsync()).ToList();
                    var allowed = CollectSubtreeIds(flat, tenantRootId.Value);
                    if (!allowed.Contains(dto.ParentId.Value))
                        return Forbid();
                }
            }

            if (dto.ParentId.HasValue)
            {
                if (dto.ParentId.Value == id)
                    return BadRequest(new { message = "A node cannot be its own parent." });
                var parent = await _service.GetByIdAsync(dto.ParentId.Value);
                if (parent == null)
                    return BadRequest(new { message = $"Parent node {dto.ParentId} does not exist." });
            }

            var node = await _service.UpdateAsync(id, dto);
            return node == null
                ? NotFound(new { message = $"Node {id} not found." })
                : Ok(node);
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            if (!await HasOrganizationActionAsync("DELETE")) return Forbid();

            var (isSuperAdmin, _, _) = await ResolveCallerContextAsync();
            if (!isSuperAdmin)
            {
                var existing = await _service.GetByIdAsync(id);
                if (existing != null && IsInfrastructureLabel(existing.Label))
                    return BadRequest(new { message = "Only Super Admin can delete Country, Group, or Company nodes." });
            }

            var (success, message) = await _service.DeleteAsync(id);
            if (!success)
                return message.Contains("not found")
                    ? NotFound(new { message })
                    : BadRequest(new { message });
            return Ok(new { message });
        }

        private static IEnumerable<OrgTreeNodeDto> PruneTreeToCompanyLevel(
            IEnumerable<OrgTreeNodeDto> nodes)
        {
            var topLabels = new[] { "Country", "Group", "Company" };
            var result = new List<OrgTreeNodeDto>();

            foreach (var n in nodes)
            {
                if (!topLabels.Contains(n.Label, StringComparer.OrdinalIgnoreCase))
                    continue;

                result.Add(new OrgTreeNodeDto
                {
                    Id       = n.Id,
                    Name     = n.Name,
                    Code     = n.Code,
                    Label    = n.Label,
                    ParentId = n.ParentId,
                    Level    = n.Level,
                    FlagUrl  = n.FlagUrl,
                    TreePath = n.TreePath,
                    Children = PruneTreeToCompanyLevel(
                        n.Children ?? Enumerable.Empty<OrgTreeNodeDto>()).ToList()
                });
            }

            return result;
        }

        ///Collect IDs of all nodes in the subtree rooted at <paramref name="rootId"/>.
        private static HashSet<int> CollectSubtreeIds(
            IEnumerable<OrgFlatTreeDto> flat, int rootId)
        {
            var list = flat.ToList();
            var ids  = new HashSet<int> { rootId };
            bool added = true;
            while (added)
            {
                added = false;
                foreach (var n in list)
                {
                    if (n.ParentId.HasValue && ids.Contains(n.ParentId.Value) && ids.Add(n.Id))
                        added = true;
                }
            }
            return ids;
        }
    }
}
