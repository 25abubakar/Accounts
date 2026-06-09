using Accounts.Authorization;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Accounts.Controllers
{
    [ApiController]
    [Route("api/organization")]
    [Authorize]                      // All endpoints require login
    [Produces("application/json")]
    public class OrganizationTreeController : ControllerBase
    {
        private readonly IOrganizationService _service;

        public OrganizationTreeController(IOrganizationService service) => _service = service;

        // ── Country Lookup — needed by forms for all authenticated users ──────

        [HttpGet("country-lookup")]
        public async Task<IActionResult> CountryLookup([FromQuery] string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return BadRequest(new { message = "Country name is required." });
            var result = await _service.CountryLookupAsync(name);
            return result == null ? NotFound(new { message = $"Country '{name}' not found." }) : Ok(result);
        }

        [HttpGet("country-search")]
        public async Task<IActionResult> CountrySearch([FromQuery] string q)
        {
            if (string.IsNullOrWhiteSpace(q)) return BadRequest(new { message = "Query 'q' is required." });
            return Ok(await _service.CountrySearchAsync(q));
        }

        // ── READ endpoints — require MENU_2017_VIEW (Master Directory menu) ──

        [HasPermission("MENU_2017_VIEW")]
        [HttpGet("tree")]
        public async Task<IActionResult> GetTree() =>
            Ok(await _service.GetTreeAsync());

        [HasPermission("MENU_2017_VIEW")]
        [HttpGet("tree/{startId:int}")]
        public async Task<IActionResult> GetSubTree(int startId)
        {
            var result = await _service.GetSubTreeAsync(startId);
            return result == null ? NotFound(new { message = $"Node {startId} not found." }) : Ok(result);
        }

        [HasPermission("MENU_2017_VIEW")]
        [HttpGet("flat-tree")]
        public async Task<IActionResult> GetFlatTree() =>
            Ok(await _service.GetFlatTreeAsync());

        [HasPermission("MENU_2017_VIEW")]
        [HttpGet]
        public async Task<IActionResult> GetAll() =>
            Ok(await _service.GetAllAsync());

        [HasPermission("MENU_2017_VIEW")]
        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id)
        {
            var node = await _service.GetByIdAsync(id);
            return node == null ? NotFound(new { message = $"Node {id} not found." }) : Ok(node);
        }

        [HasPermission("MENU_2017_VIEW")]
        [HttpGet("by-label/{label}")]
        public async Task<IActionResult> GetByLabel(string label) =>
            Ok(await _service.GetByLabelAsync(label));

        [HasPermission("MENU_2017_VIEW")]
        [HttpGet("{id:int}/children")]
        public async Task<IActionResult> GetChildren(int id)
        {
            var children = await _service.GetChildrenAsync(id);
            return children == null ? NotFound(new { message = $"Node {id} not found." }) : Ok(children);
        }

        [HasPermission("MENU_2017_VIEW")]
        [HttpGet("search")]
        public async Task<IActionResult> Search([FromQuery] string q)
        {
            if (string.IsNullOrWhiteSpace(q)) return BadRequest(new { message = "Query 'q' is required." });
            return Ok(await _service.SearchAsync(q));
        }

        // ── WRITE endpoints — require specific CRUD permissions ───────────────

        [HasPermission("MENU_2017_ADD")]
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

        [HasPermission("MENU_2017_EDIT")]
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

        [HasPermission("MENU_2017_DELETE")]
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var (success, message) = await _service.DeleteAsync(id);
            if (!success) return message.Contains("not found") ? NotFound(new { message }) : BadRequest(new { message });
            return Ok(new { message });
        }
    }
}
