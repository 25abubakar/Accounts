using Accounts.Models;
using Accounts.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Accounts.Controllers
{
    [ApiController]
    [Route("api/organization")]
    [Produces("application/json")]
    public class OrganizationTreeController : ControllerBase
    {
        private readonly IOrganizationService _service;

        public OrganizationTreeController(IOrganizationService service) => _service = service;

        // ── Country Lookup ────────────────────────────────────────────────────

        /// <summary>Lookup country info (flag, code, capital) before creating a Country node</summary>
        [HttpGet("country-lookup")]
        public async Task<IActionResult> CountryLookup([FromQuery] string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return BadRequest(new { message = "Country name is required." });
            var result = await _service.CountryLookupAsync(name);
            return result == null ? NotFound(new { message = $"Country '{name}' not found." }) : Ok(result);
        }

        /// <summary>Search countries by name — for autocomplete dropdown</summary>
        [HttpGet("country-search")]
        public async Task<IActionResult> CountrySearch([FromQuery] string q)
        {
            if (string.IsNullOrWhiteSpace(q)) return BadRequest(new { message = "Query 'q' is required." });
            return Ok(await _service.CountrySearchAsync(q));
        }

        // ── Tree ──────────────────────────────────────────────────────────────

        /// <summary>Full hierarchy as nested JSON tree</summary>
        [HttpGet("tree")]
        public async Task<IActionResult> GetTree() =>
            Ok(await _service.GetTreeAsync());

        /// <summary>Subtree from any node (e.g. /tree/2 = TechSoft and all children)</summary>
        [HttpGet("tree/{startId:int}")]
        public async Task<IActionResult> GetSubTree(int startId)
        {
            var result = await _service.GetSubTreeAsync(startId);
            return result == null ? NotFound(new { message = $"Node {startId} not found." }) : Ok(result);
        }

        /// <summary>Flat list with Level, TreePath and indented TreeStructure</summary>
        [HttpGet("flat-tree")]
        public async Task<IActionResult> GetFlatTree() =>
            Ok(await _service.GetFlatTreeAsync());

        // ── CRUD ──────────────────────────────────────────────────────────────

        /// <summary>Get all nodes as flat list</summary>
        [HttpGet]
        public async Task<IActionResult> GetAll() =>
            Ok(await _service.GetAllAsync());

        /// <summary>Get a single node by ID</summary>
        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id)
        {
            var node = await _service.GetByIdAsync(id);
            return node == null ? NotFound(new { message = $"Node {id} not found." }) : Ok(node);
        }

        /// <summary>Filter nodes by label (Country / Company / Branch / Group / etc.)</summary>
        [HttpGet("by-label/{label}")]
        public async Task<IActionResult> GetByLabel(string label) =>
            Ok(await _service.GetByLabelAsync(label));

        /// <summary>Get direct children of a node</summary>
        [HttpGet("{id:int}/children")]
        public async Task<IActionResult> GetChildren(int id)
        {
            var children = await _service.GetChildrenAsync(id);
            return children == null ? NotFound(new { message = $"Node {id} not found." }) : Ok(children);
        }

        /// <summary>Search nodes by name (partial, case-insensitive)</summary>
        [HttpGet("search")]
        public async Task<IActionResult> Search([FromQuery] string q)
        {
            if (string.IsNullOrWhiteSpace(q)) return BadRequest(new { message = "Query 'q' is required." });
            return Ok(await _service.SearchAsync(q));
        }

        /// <summary>
        /// Create any node with any label (Country, Group, Company, Branch, Department, etc.).
        /// For Country nodes, flag and code are auto-fetched if not provided.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateOrgNodeDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            if (dto.ParentId.HasValue)
            {
                var parent = await _service.GetByIdAsync(dto.ParentId.Value);
                if (parent == null) return BadRequest(new { message = $"Parent node {dto.ParentId} does not exist." });
            }

            var (node, _) = await _service.CreateAsync(dto);
            return CreatedAtAction(nameof(GetById), new { id = node.Id }, node);
        }

        /// <summary>Update a node — name, code, label, parent, flagUrl</summary>
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] UpdateOrgNodeDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            if (dto.ParentId.HasValue)
            {
                if (dto.ParentId.Value == id) return BadRequest(new { message = "A node cannot be its own parent." });
                var parent = await _service.GetByIdAsync(dto.ParentId.Value);
                if (parent == null) return BadRequest(new { message = $"Parent node {dto.ParentId} does not exist." });
            }

            var node = await _service.UpdateAsync(id, dto);
            return node == null ? NotFound(new { message = $"Node {id} not found." }) : Ok(node);
        }

        /// <summary>Delete a node — blocked if it has children or vacancies</summary>
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var (success, message) = await _service.DeleteAsync(id);
            if (!success) return message.Contains("not found") ? NotFound(new { message }) : BadRequest(new { message });
            return Ok(new { message });
        }
    }
}
