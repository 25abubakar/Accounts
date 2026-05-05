using Accounts.Data;
using Accounts.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public class OrganizationTreeController : ControllerBase
    {
        private readonly ApplicationDbContext _db;

        public OrganizationTreeController(ApplicationDbContext db) => _db = db;

        // ─────────────────────────────────────────────────────────────
        // GET /api/organizationtree
        // All nodes as flat list
        // ─────────────────────────────────────────────────────────────
        /// <summary>Get all nodes as a flat list</summary>
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var nodes = await _db.OrganizationTree
                .Include(n => n.Parent)
                .OrderBy(n => n.Id)
                .Select(n => new OrgNodeDto
                {
                    Id         = n.Id,
                    Name       = n.Name,
                    Code       = n.Code,
                    Label      = n.Label,
                    ParentId   = n.ParentId,
                    ParentName = n.Parent != null ? n.Parent.Name : null
                })
                .ToListAsync();

            return Ok(nodes);
        }

        // ─────────────────────────────────────────────────────────────
        // GET /api/organizationtree/{id}
        // Single node by ID
        // ─────────────────────────────────────────────────────────────
        /// <summary>Get a single node by ID</summary>
        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id)
        {
            var node = await _db.OrganizationTree
                .Include(n => n.Parent)
                .Where(n => n.Id == id)
                .Select(n => new OrgNodeDto
                {
                    Id         = n.Id,
                    Name       = n.Name,
                    Code       = n.Code,
                    Label      = n.Label,
                    ParentId   = n.ParentId,
                    ParentName = n.Parent != null ? n.Parent.Name : null
                })
                .FirstOrDefaultAsync();

            if (node == null)
                return NotFound(new { message = $"Node {id} not found." });

            return Ok(node);
        }

        // ─────────────────────────────────────────────────────────────
        // GET /api/organizationtree/tree
        // Full hierarchy as nested JSON (Country → Company → Branch → Staff)
        // ─────────────────────────────────────────────────────────────
        /// <summary>Full hierarchy as nested JSON tree</summary>
        [HttpGet("tree")]
        public async Task<IActionResult> GetTree()
        {
            var all = await _db.OrganizationTree.ToListAsync();
            var tree = BuildNestedTree(all, null, 0, "");
            return Ok(tree);
        }

        // ─────────────────────────────────────────────────────────────
        // GET /api/organizationtree/tree/{startId}
        // Subtree from any node (e.g. TechSoft = 2)
        // ─────────────────────────────────────────────────────────────
        /// <summary>Get subtree starting from a specific node (e.g. /tree/2 = TechSoft subtree)</summary>
        [HttpGet("tree/{startId:int}")]
        public async Task<IActionResult> GetSubTree(int startId)
        {
            var startNode = await _db.OrganizationTree.FindAsync(startId);
            if (startNode == null)
                return NotFound(new { message = $"Node {startId} not found." });

            var all = await _db.OrganizationTree.ToListAsync();

            var ids = new HashSet<int> { startId };
            CollectDescendants(all, startId, ids);

            var subtree = all.Where(n => ids.Contains(n.Id)).ToList();
            var tree = BuildNestedTree(subtree, startNode.ParentId, 0, "");
            return Ok(tree);
        }

        // ─────────────────────────────────────────────────────────────
        // GET /api/organizationtree/flat-tree
        // Flat list with Level + TreePath + indented TreeStructure
        // Mirrors the SQL CTE result
        // ─────────────────────────────────────────────────────────────
        /// <summary>Flat list with Level, TreePath and indented TreeStructure — mirrors SQL CTE</summary>
        [HttpGet("flat-tree")]
        public async Task<IActionResult> GetFlatTree()
        {
            var all = await _db.OrganizationTree.ToListAsync();
            var result = new List<OrgFlatTreeDto>();
            BuildFlatTree(all, null, 0, "", result);
            return Ok(result);
        }

        // ─────────────────────────────────────────────────────────────
        // GET /api/organizationtree/by-label/{label}
        // Filter by label: Country / Company / Branch / Staff
        // ─────────────────────────────────────────────────────────────
        /// <summary>Filter nodes by label (Country / Company / Branch / Staff)</summary>
        [HttpGet("by-label/{label}")]
        public async Task<IActionResult> GetByLabel(string label)
        {
            var nodes = await _db.OrganizationTree
                .Include(n => n.Parent)
                .Where(n => n.Label.ToLower() == label.ToLower())
                .Select(n => new OrgNodeDto
                {
                    Id         = n.Id,
                    Name       = n.Name,
                    Code       = n.Code,
                    Label      = n.Label,
                    ParentId   = n.ParentId,
                    ParentName = n.Parent != null ? n.Parent.Name : null
                })
                .ToListAsync();

            return Ok(nodes);
        }

        // ─────────────────────────────────────────────────────────────
        // GET /api/organizationtree/{id}/children
        // Direct children of a node
        // ─────────────────────────────────────────────────────────────
        /// <summary>Get direct children of a node</summary>
        [HttpGet("{id:int}/children")]
        public async Task<IActionResult> GetChildren(int id)
        {
            if (!await _db.OrganizationTree.AnyAsync(n => n.Id == id))
                return NotFound(new { message = $"Node {id} not found." });

            var children = await _db.OrganizationTree
                .Include(n => n.Parent)
                .Where(n => n.ParentId == id)
                .Select(n => new OrgNodeDto
                {
                    Id         = n.Id,
                    Name       = n.Name,
                    Code       = n.Code,
                    Label      = n.Label,
                    ParentId   = n.ParentId,
                    ParentName = n.Parent != null ? n.Parent.Name : null
                })
                .ToListAsync();

            return Ok(children);
        }

        // ─────────────────────────────────────────────────────────────
        // GET /api/organizationtree/search?q=ali
        // Search by name
        // ─────────────────────────────────────────────────────────────
        /// <summary>Search nodes by name (partial, case-insensitive)</summary>
        [HttpGet("search")]
        public async Task<IActionResult> Search([FromQuery] string q)
        {
            if (string.IsNullOrWhiteSpace(q))
                return BadRequest(new { message = "Query 'q' is required." });

            var nodes = await _db.OrganizationTree
                .Include(n => n.Parent)
                .Where(n => n.Name.Contains(q))
                .Select(n => new OrgNodeDto
                {
                    Id         = n.Id,
                    Name       = n.Name,
                    Code       = n.Code,
                    Label      = n.Label,
                    ParentId   = n.ParentId,
                    ParentName = n.Parent != null ? n.Parent.Name : null
                })
                .ToListAsync();

            return Ok(nodes);
        }

        // ─────────────────────────────────────────────────────────────
        // POST /api/organizationtree
        // Create a new node (auto-generates next ID)
        // ─────────────────────────────────────────────────────────────
        /// <summary>Create a new node (ID is auto-generated)</summary>
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateOrgNodeDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            if (dto.ParentId.HasValue &&
                !await _db.OrganizationTree.AnyAsync(n => n.Id == dto.ParentId.Value))
                return BadRequest(new { message = $"Parent node {dto.ParentId} does not exist." });

            var nextId = (await _db.OrganizationTree.MaxAsync(n => (int?)n.Id) ?? 0) + 1;

            var node = new OrganizationTree
            {
                Id       = nextId,
                Name     = dto.Name,
                Code     = dto.Code,
                Label    = dto.Label,
                ParentId = dto.ParentId
            };

            _db.OrganizationTree.Add(node);
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = node.Id },
                new OrgNodeDto { Id = node.Id, Name = node.Name, Code = node.Code, Label = node.Label, ParentId = node.ParentId });
        }

        // ─────────────────────────────────────────────────────────────
        // PUT /api/organizationtree/{id}
        // Update a node
        // ─────────────────────────────────────────────────────────────
        /// <summary>Update an existing node</summary>
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] UpdateOrgNodeDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var node = await _db.OrganizationTree.FindAsync(id);
            if (node == null)
                return NotFound(new { message = $"Node {id} not found." });

            if (dto.ParentId.HasValue)
            {
                if (dto.ParentId.Value == id)
                    return BadRequest(new { message = "A node cannot be its own parent." });

                if (!await _db.OrganizationTree.AnyAsync(n => n.Id == dto.ParentId.Value))
                    return BadRequest(new { message = $"Parent node {dto.ParentId} does not exist." });
            }

            node.Name     = dto.Name;
            node.Code     = dto.Code;
            node.Label    = dto.Label;
            node.ParentId = dto.ParentId;

            await _db.SaveChangesAsync();

            return Ok(new OrgNodeDto { Id = node.Id, Name = node.Name, Code = node.Code, Label = node.Label, ParentId = node.ParentId });
        }

        // ─────────────────────────────────────────────────────────────
        // DELETE /api/organizationtree/{id}
        // Delete a node (blocked if it has children)
        // ─────────────────────────────────────────────────────────────
        /// <summary>Delete a node — blocked if it has children (delete children first)</summary>
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var node = await _db.OrganizationTree.FindAsync(id);
            if (node == null)
                return NotFound(new { message = $"Node {id} not found." });

            if (await _db.OrganizationTree.AnyAsync(n => n.ParentId == id))
                return BadRequest(new { message = "Cannot delete — this node has children. Delete children first." });

            _db.OrganizationTree.Remove(node);
            await _db.SaveChangesAsync();

            return Ok(new { message = $"Node '{node.Name}' (ID: {id}) deleted." });
        }

        // ─────────────────────────────────────────────────────────────
        // Private helpers
        // ─────────────────────────────────────────────────────────────

        private static List<OrgTreeNodeDto> BuildNestedTree(
            List<OrganizationTree> all, int? parentId, int level, string parentPath)
        {
            return all
                .Where(n => n.ParentId == parentId)
                .OrderBy(n => n.Id)
                .Select(n =>
                {
                    var display = (n.Code != null ? $"[{n.Code}] " : "") + n.Name;
                    var path    = string.IsNullOrEmpty(parentPath) ? display : $"{parentPath} → {display}";
                    return new OrgTreeNodeDto
                    {
                        Id       = n.Id,
                        Name     = n.Name,
                        Code     = n.Code,
                        Label    = n.Label,
                        ParentId = n.ParentId,
                        Level    = level,
                        TreePath = path,
                        Children = BuildNestedTree(all, n.Id, level + 1, path)
                    };
                })
                .ToList();
        }

        private static void BuildFlatTree(
            List<OrganizationTree> all, int? parentId, int level,
            string parentPath, List<OrgFlatTreeDto> result)
        {
            foreach (var n in all.Where(n => n.ParentId == parentId).OrderBy(n => n.Id))
            {
                var display = (n.Code != null ? $"[{n.Code}] " : "") + n.Name;
                var path    = string.IsNullOrEmpty(parentPath) ? display : $"{parentPath} → {display}";
                var indent  = new string(' ', level * 3) + display;

                result.Add(new OrgFlatTreeDto
                {
                    Id             = n.Id,
                    Name           = n.Name,
                    Code           = n.Code,
                    Label          = n.Label,
                    ParentId       = n.ParentId,
                    Level          = level,
                    TreePath       = path,
                    TreeStructure  = indent
                });

                BuildFlatTree(all, n.Id, level + 1, path, result);
            }
        }

        private static void CollectDescendants(
            List<OrganizationTree> all, int parentId, HashSet<int> ids)
        {
            foreach (var child in all.Where(n => n.ParentId == parentId))
            {
                ids.Add(child.Id);
                CollectDescendants(all, child.Id, ids);
            }
        }
    }
}
