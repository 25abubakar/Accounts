using Accounts.Authorization;
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
    [Route("api/organization")]
    [Authorize]
    [Produces("application/json")]
    public class OrganizationTreeController : ControllerBase
    {
        private readonly IOrganizationService         _service;
        private readonly ApplicationDbContext         _db;
        private readonly UserManager<ApplicationUser> _userManager;

        public OrganizationTreeController(
            IOrganizationService          service,
            ApplicationDbContext          db,
            UserManager<ApplicationUser>  userManager)
        {
            _service     = service;
            _db          = db;
            _userManager = userManager;
        }

        // ── Caller context helper ─────────────────────────────────────────────

        /// <summary>
        /// Resolves the calling user's role and their root org node:
        ///   Super Admin  → tree pruned to Country / Group / Company level only
        ///   Tenant Admin → subtree starting from their company root node
        ///   Staff Member → subtree starting from their company root node (via Vacancy)
        /// </summary>
        private async Task<(bool isSuperAdmin, bool isTenantAdmin, int? tenantRootNodeId)>
            ResolveCallerContextAsync()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId))
                return (false, false, null);

            var appUser = await _userManager.FindByIdAsync(userId);
            if (appUser == null) return (false, false, null);

            int? rootNodeId = null;

            if (appUser.IsTenantAdmin && appUser.TenantId.HasValue)
            {
                // Tenant Admin: root = the company/group node linked to their tenant
                var tenant = await _db.Tenants.AsNoTracking()
                    .FirstOrDefaultAsync(t => t.Id == appUser.TenantId.Value);
                rootNodeId = tenant?.OrganizationTreeId;
            }
            else if (!appUser.IsSuperAdmin && appUser.TenantId.HasValue)
            {
                // Regular Staff: root = the company node belonging to their tenant
                var tenant = await _db.Tenants.AsNoTracking()
                    .FirstOrDefaultAsync(t => t.Id == appUser.TenantId.Value);
                rootNodeId = tenant?.OrganizationTreeId;
            }

            return (appUser.IsSuperAdmin, appUser.IsTenantAdmin, rootNodeId);
        }

        // ── Country Lookup — available to all authenticated users ─────────────

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

        // ── READ ──────────────────────────────────────────────────────────────

        [HttpGet("tree")]
        public async Task<IActionResult> GetTree()
        {
            var (isSuperAdmin, isTenantAdmin, tenantRootId) = await ResolveCallerContextAsync();
            var tree = await _service.GetTreeAsync();

            if (isSuperAdmin)
                return Ok(PruneTreeToCompanyLevel(tree));

            // Both Tenant Admin and regular Staff see their company subtree only
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

            // Both Tenant Admin and regular Staff are scoped to their company subtree
            if (tenantRootId.HasValue)
            {
                var subtreeIds = CollectSubtreeIds(flat, tenantRootId.Value);
                // Include ancestor chain so breadcrumbs and country/group context is visible
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
                // Tenant Admin and regular Staff: return their subtree + ancestors
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

        // ── WRITE ─────────────────────────────────────────────────────────────
        //
        // Business rules:
        //   Super Admin  → can add any node (Country/Group/Company) at the top level.
        //                  Company creation should go through POST /api/tenants
        //                  but plain org nodes are still allowed here.
        //   Tenant Admin → can add Branch / Department / Team ONLY under their
        //                  own company subtree. Country/Group/Company labels are
        //                  forbidden (must use POST /api/tenants).
        //   Staff        → RBAC permission check only (HasPermission attribute).

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateOrgNodeDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var (isSuperAdmin, isTenantAdmin, tenantRootId) = await ResolveCallerContextAsync();

            // Tenant Admin and regular Staff cannot create top-level infrastructure nodes
            if (!isSuperAdmin)
            {
                var blocked = new[] { "Country", "Group", "Company" };
                if (blocked.Contains(dto.Label, StringComparer.OrdinalIgnoreCase))
                    return Forbid();

                // Must place the node within their own subtree
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
            var (success, message) = await _service.DeleteAsync(id);
            if (!success)
                return message.Contains("not found")
                    ? NotFound(new { message })
                    : BadRequest(new { message });
            return Ok(new { message });
        }

        // ── Private helpers ───────────────────────────────────────────────────

        /// <summary>Recursively prune tree nodes deeper than Company level.</summary>
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

        /// <summary>Collect IDs of all nodes in the subtree rooted at <paramref name="rootId"/>.</summary>
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
