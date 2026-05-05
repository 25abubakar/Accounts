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

        public OrganizationTreeController(ApplicationDbContext db)
        {
            _db = db;
        }

        // ─────────────────────────────────────────────────────────────
        // GET /api/organizationtree
        // Returns all nodes as a flat list
        // ─────────────────────────────────────────────────────────────
        /// <summary>Get all organization nodes (flat list)</summary>
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var nodes = await _db.OrganizationTree
                .Include(n => n.Parent)
                .OrderBy(n => n.Id)
                .Select(n => new OrgNodeDto
                {
                    Id = n.Id,
                    Name = n.Name,
                    Code = n.Code,
                    Label = n.Label,
                    ParentId = n.ParentId,
                    ParentName = n.Parent != null ? n.Parent.Name : null
                })
                .ToListAsync();

            return Ok(nodes);
        }

        // ─────────────────────────────────────────────────────────────
        // GET /api/organizationtree/{id}
        // Returns a single node by ID
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
                    Id = n.Id,
                    Name = n.Name,
                    Code = n.Code,
                    Label = n.Label,
                    ParentId = n.ParentId,
                    ParentName = n.Parent != null ? n.Parent.Name : null
                })
                .FirstOrDefaultAsync();

            if (node == null)
                return NotFound(new { message = $"Node with ID {id} not found." });

            return Ok(node);
        }

        // ─────────────────────────────────────────────────────────────
        // GET /api/organizationtree/tree
        // Returns full hierarchy as nested JSON tree
        // ─────────────────────────────────────────────────────────────
        /// <summary>Get full organization as a nested tree (root → children → grandchildren)</summary>
        [HttpGet("tree")]
        public async Task<IActionResult> GetTree()
        {
            var allNodes = await _db.OrganizationTree.ToListAsync();
            var tree = BuildNestedTree(allNodes, null, 0, "");
            return Ok(tree);
        }

        // ─────────────────────────────────────────────────────────────
        // GET /api/organizationtree/tree/{startId}
        // Returns subtree starting from a specific node
        // ─────────────────────────────────────────────────────────────
        /// <summary>Get subtree starting from a specific node ID (e.g. a Company or Branch)</summary>
        [HttpGet("tree/{startId:int}")]
        public async Task<IActionResult> GetSubTree(int startId)
        {
            var startNode = await _db.OrganizationTree.FindAsync(startId);
            if (startNode == null)
                return NotFound(new { message = $"Node with ID {startId} not found." });

            var allNodes = await _db.OrganizationTree.ToListAsync();

            // Collect all descendants of startId
            var subtreeIds = new HashSet<int>();
            CollectDescendants(allNodes, startId, subtreeIds);
            subtreeIds.Add(startId);

            var subtreeNodes = allNodes.Where(n => subtreeIds.Contains(n.Id)).ToList();
            var tree = BuildNestedTree(subtreeNodes, startNode.ParentId, 0, "");
            return Ok(tree);
        }

        // ─────────────────────────────────────────────────────────────
        // GET /api/organizationtree/flat-tree
        // Returns flat list with Level and TreePath (mirrors CTE SQL)
        // ─────────────────────────────────────────────────────────────
        /// <summary>Get flat tree with Level and TreePath columns (mirrors the SQL CTE result)</summary>
        [HttpGet("flat-tree")]
        public async Task<IActionResult> GetFlatTree()
        {
            var allNodes = await _db.OrganizationTree.ToListAsync();
            var result = new List<OrgFlatTreeDto>();
            BuildFlatTree(allNodes, null, 0, "", result);
            return Ok(result);
        }

        // ─────────────────────────────────────────────────────────────
        // GET /api/organizationtree/by-label/{label}
        // Filter nodes by label: Country / Company / Branch / Staff
        // ─────────────────────────────────────────────────────────────
        /// <summary>Get all nodes filtered by label (Country / Company / Branch / Staff)</summary>
        [HttpGet("by-label/{label}")]
        public async Task<IActionResult> GetByLabel(string label)
        {
            var nodes = await _db.OrganizationTree
                .Include(n => n.Parent)
                .Where(n => n.Label.ToLower() == label.ToLower())
                .Select(n => new OrgNodeDto
                {
                    Id = n.Id,
                    Name = n.Name,
                    Code = n.Code,
                    Label = n.Label,
                    ParentId = n.ParentId,
                    ParentName = n.Parent != null ? n.Parent.Name : null
                })
                .ToListAsync();

            return Ok(nodes);
        }

        // ─────────────────────────────────────────────────────────────
        // GET /api/organizationtree/{id}/children
        // Returns direct children of a node
        // ─────────────────────────────────────────────────────────────
        /// <summary>Get direct children of a node</summary>
        [HttpGet("{id:int}/children")]
        public async Task<IActionResult> GetChildren(int id)
        {
            var exists = await _db.OrganizationTree.AnyAsync(n => n.Id == id);
            if (!exists)
                return NotFound(new { message = $"Node with ID {id} not found." });

            var children = await _db.OrganizationTree
                .Where(n => n.ParentId == id)
                .Select(n => new OrgNodeDto
                {
                    Id = n.Id,
                    Name = n.Name,
                    Code = n.Code,
                    Label = n.Label,
                    ParentId = n.ParentId,
                    ParentName = n.Name
                })
                .ToListAsync();

            return Ok(children);
        }

        // ─────────────────────────────────────────────────────────────
        // POST /api/organizationtree
        // Create a new node
        // ─────────────────────────────────────────────────────────────
        /// <summary>Create a new organization node</summary>
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateOrgNodeDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            // Validate parent exists if provided
            if (dto.ParentId.HasValue)
            {
                var parentExists = await _db.OrganizationTree.AnyAsync(n => n.Id == dto.ParentId.Value);
                if (!parentExists)
                    return BadRequest(new { message = $"Parent node with ID {dto.ParentId} does not exist." });
            }

            // Auto-generate next ID
            var nextId = await _db.OrganizationTree.MaxAsync(n => (int?)n.Id) ?? 0;
            nextId++;

            var node = new OrganizationTree
            {
                Id = nextId,
                Name = dto.Name,
                Code = dto.Code,
                Label = dto.Label,
                ParentId = dto.ParentId
            };

            _db.OrganizationTree.Add(node);
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = node.Id }, new OrgNodeDto
            {
                Id = node.Id,
                Name = node.Name,
                Code = node.Code,
                Label = node.Label,
                ParentId = node.ParentId
            });
        }

        // ─────────────────────────────────────────────────────────────
        // PUT /api/organizationtree/{id}
        // Update an existing node
        // ─────────────────────────────────────────────────────────────
        /// <summary>Update an existing organization node</summary>
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] UpdateOrgNodeDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var node = await _db.OrganizationTree.FindAsync(id);
            if (node == null)
                return NotFound(new { message = $"Node with ID {id} not found." });

            // Prevent circular reference (node cannot be its own parent or ancestor)
            if (dto.ParentId.HasValue)
            {
                if (dto.ParentId.Value == id)
                    return BadRequest(new { message = "A node cannot be its own parent." });

                var parentExists = await _db.OrganizationTree.AnyAsync(n => n.Id == dto.ParentId.Value);
                if (!parentExists)
                    return BadRequest(new { message = $"Parent node with ID {dto.ParentId} does not exist." });
            }

            node.Name = dto.Name;
            node.Code = dto.Code;
            node.Label = dto.Label;
            node.ParentId = dto.ParentId;

            await _db.SaveChangesAsync();

            return Ok(new OrgNodeDto
            {
                Id = node.Id,
                Name = node.Name,
                Code = node.Code,
                Label = node.Label,
                ParentId = node.ParentId
            });
        }

        // ─────────────────────────────────────────────────────────────
        // DELETE /api/organizationtree/{id}
        // Delete a node (only if it has no children)
        // ─────────────────────────────────────────────────────────────
        /// <summary>Delete a node. Fails if the node has children — delete children first.</summary>
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var node = await _db.OrganizationTree.FindAsync(id);
            if (node == null)
                return NotFound(new { message = $"Node with ID {id} not found." });

            var hasChildren = await _db.OrganizationTree.AnyAsync(n => n.ParentId == id);
            if (hasChildren)
                return BadRequest(new { message = "Cannot delete a node that has children. Delete children first." });

            _db.OrganizationTree.Remove(node);
            await _db.SaveChangesAsync();

            return Ok(new { message = $"Node '{node.Name}' (ID: {id}) deleted successfully." });
        }

        // ─────────────────────────────────────────────────────────────
        // GET /api/organizationtree/search?q=ali
        // Search nodes by name
        // ─────────────────────────────────────────────────────────────
        /// <summary>Search nodes by name (case-insensitive partial match)</summary>
        [HttpGet("search")]
        public async Task<IActionResult> Search([FromQuery] string q)
        {
            if (string.IsNullOrWhiteSpace(q))
                return BadRequest(new { message = "Search query 'q' is required." });

            var nodes = await _db.OrganizationTree
                .Include(n => n.Parent)
                .Where(n => n.Name.Contains(q))
                .Select(n => new OrgNodeDto
                {
                    Id = n.Id,
                    Name = n.Name,
                    Code = n.Code,
                    Label = n.Label,
                    ParentId = n.ParentId,
                    ParentName = n.Parent != null ? n.Parent.Name : null
                })
                .ToListAsync();

            return Ok(nodes);
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
                    var path = string.IsNullOrEmpty(parentPath) ? display : $"{parentPath} → {display}";
                    return new OrgTreeNodeDto
                    {
                        Id = n.Id,
                        Name = n.Name,
                        Code = n.Code,
                        Label = n.Label,
                        ParentId = n.ParentId,
                        Level = level,
                        TreePath = path,
                        Children = BuildNestedTree(all, n.Id, level + 1, path)
                    };
                })
                .ToList();
        }

        private static void BuildFlatTree(
            List<OrganizationTree> all, int? parentId, int level, string parentPath,
            List<OrgFlatTreeDto> result)
        {
            var children = all.Where(n => n.ParentId == parentId).OrderBy(n => n.Id);
            foreach (var n in children)
            {
                var display = (n.Code != null ? $"[{n.Code}] " : "") + n.Name;
                var path = string.IsNullOrEmpty(parentPath) ? display : $"{parentPath} → {display}";
                var indent = new string(' ', level * 3) + (n.Code != null ? $"[{n.Code}] " : "") + n.Name;

                result.Add(new OrgFlatTreeDto
                {
                    Id = n.Id,
                    Name = n.Name,
                    Code = n.Code,
                    Label = n.Label,
                    ParentId = n.ParentId,
                    Level = level,
                    TreePath = path,
                    TreeStructure = indent
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
