using Accounts.Data;
using Accounts.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Accounts.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public class OrganizationTreeController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IHttpClientFactory _httpClientFactory;

        public OrganizationTreeController(
            ApplicationDbContext db,
            IHttpClientFactory httpClientFactory)
        {
            _db = db;
            _httpClientFactory = httpClientFactory;
        }

        // COUNTRY LOOKUP — auto-fetch flag + code from restcountries.com

        /// <summary>
        /// Lookup country info by name — returns code, flag URL, capital, region.
        /// Call this before creating a Country node to auto-fill code and flag.
        /// Example: GET /api/organizationtree/country-lookup?name=Pakistan
        /// </summary>
        [HttpGet("country-lookup")]
        public async Task<IActionResult> CountryLookup([FromQuery] string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return BadRequest(new { message = "Country name is required." });

            try
            {
                var client = _httpClientFactory.CreateClient("CountryApi");
                var response = await client.GetAsync($"name/{Uri.EscapeDataString(name)}?fullText=false&fields=name,cca2,cca3,flags,region,capital");

                if (!response.IsSuccessStatusCode)
                    return NotFound(new { message = $"Country '{name}' not found in restcountries.com." });

                var json = await response.Content.ReadAsStringAsync();
                var countries = JsonSerializer.Deserialize<JsonElement[]>(json);

                if (countries == null || countries.Length == 0)
                    return NotFound(new { message = $"No results for '{name}'." });

                // Take best match
                var c = countries[0];

                var result = new CountryLookupDto
                {
                    Name    = c.GetProperty("name").GetProperty("common").GetString() ?? name,
                    Code    = c.GetProperty("cca2").GetString() ?? "",
                    Code3   = c.GetProperty("cca3").GetString() ?? "",
                    FlagUrl = c.GetProperty("flags").GetProperty("svg").GetString() ?? "",
                    FlagPng = c.GetProperty("flags").GetProperty("png").GetString() ?? "",
                    Region  = c.TryGetProperty("region", out var region) ? region.GetString() ?? "" : "",
                    Capital = c.TryGetProperty("capital", out var cap) && cap.ValueKind == JsonValueKind.Array
                              ? cap[0].GetString() ?? "" : ""
                };

                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to fetch country data.", error = ex.Message });
            }
        }

        /// <summary>
        /// Search multiple countries — useful for autocomplete dropdown.
        /// Example: GET /api/organizationtree/country-search?q=pak
        /// </summary>
        [HttpGet("country-search")]
        public async Task<IActionResult> CountrySearch([FromQuery] string q)
        {
            if (string.IsNullOrWhiteSpace(q))
                return BadRequest(new { message = "Query 'q' is required." });

            try
            {
                var client = _httpClientFactory.CreateClient("CountryApi");
                var response = await client.GetAsync($"name/{Uri.EscapeDataString(q)}?fields=name,cca2,cca3,flags,region,capital");

                if (!response.IsSuccessStatusCode)
                    return Ok(new List<CountryLookupDto>());

                var json = await response.Content.ReadAsStringAsync();
                var countries = JsonSerializer.Deserialize<JsonElement[]>(json);

                if (countries == null) return Ok(new List<CountryLookupDto>());

                var results = countries.Take(10).Select(c => new CountryLookupDto
                {
                    Name    = c.GetProperty("name").GetProperty("common").GetString() ?? "",
                    Code    = c.GetProperty("cca2").GetString() ?? "",
                    Code3   = c.GetProperty("cca3").GetString() ?? "",
                    FlagUrl = c.GetProperty("flags").GetProperty("svg").GetString() ?? "",
                    FlagPng = c.GetProperty("flags").GetProperty("png").GetString() ?? "",
                    Region  = c.TryGetProperty("region", out var region) ? region.GetString() ?? "" : "",
                    Capital = c.TryGetProperty("capital", out var cap) && cap.ValueKind == JsonValueKind.Array
                              ? cap[0].GetString() ?? "" : ""
                }).ToList();

                return Ok(results);
            }
            catch
            {
                return Ok(new List<CountryLookupDto>());
            }
        }

        // TREE / HIERARCHY ENDPOINTS

        /// <summary>Full hierarchy as nested JSON tree</summary>
        [HttpGet("tree")]
        public async Task<IActionResult> GetTree()
        {
            var all = await _db.OrganizationTree.ToListAsync();
            return Ok(BuildNestedTree(all, null, 0, ""));
        }

        /// <summary>Subtree from any node (e.g. /tree/2 = TechSoft and all children)</summary>
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
            return Ok(BuildNestedTree(subtree, startNode.ParentId, 0, ""));
        }

        /// <summary>Flat list with Level, TreePath and indented TreeStructure</summary>
        [HttpGet("flat-tree")]
        public async Task<IActionResult> GetFlatTree()
        {
            var all = await _db.OrganizationTree.ToListAsync();
            var result = new List<OrgFlatTreeDto>();
            BuildFlatTree(all, null, 0, "", result);
            return Ok(result);
        }

        /// <summary>Get direct children of any node</summary>
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
                    ParentName = n.Parent != null ? n.Parent.Name : null,
                    FlagUrl    = n.FlagUrl
                })
                .ToListAsync();

            return Ok(children);
        }

        /// <summary>Search all nodes by name</summary>
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
                    ParentName = n.Parent != null ? n.Parent.Name : null,
                    FlagUrl    = n.FlagUrl
                })
                .ToListAsync();

            return Ok(nodes);
        }

        // GENERIC NODE CRUD — works for ANY label
        // (Country, Group, Company, Division, Region, Branch, Team, etc.)

        /// <summary>Get all nodes as flat list</summary>
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
                    ParentName = n.Parent != null ? n.Parent.Name : null,
                    FlagUrl    = n.FlagUrl
                })
                .ToListAsync();
            return Ok(nodes);
        }

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
                    ParentName = n.Parent != null ? n.Parent.Name : null,
                    FlagUrl    = n.FlagUrl
                })
                .FirstOrDefaultAsync();

            if (node == null) return NotFound(new { message = $"Node {id} not found." });
            return Ok(node);
        }

        /// <summary>Filter nodes by label (any label value)</summary>
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
                    ParentName = n.Parent != null ? n.Parent.Name : null,
                    FlagUrl    = n.FlagUrl
                })
                .ToListAsync();
            return Ok(nodes);
        }

        /// <summary>
        /// Create any node with any label.
        /// For Label = "Country": if FlagUrl is empty, it auto-fetches from restcountries.com.
        /// Supported labels (not limited): Country, Group, Company, Division,
        /// Region, Branch, Department, Team, Staff, etc.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateOrgNodeDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            if (dto.ParentId.HasValue &&
                !await _db.OrganizationTree.AnyAsync(n => n.Id == dto.ParentId.Value))
                return BadRequest(new { message = $"Parent node {dto.ParentId} does not exist." });

            var nextId = (await _db.OrganizationTree.MaxAsync(n => (int?)n.Id) ?? 0) + 1;

            string? flagUrl = dto.FlagUrl;
            string? code    = dto.Code;

            // Auto-fetch flag + code for Country nodes
            if (dto.Label.Equals("Country", StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrWhiteSpace(flagUrl))
            {
                try
                {
                    var client = _httpClientFactory.CreateClient("CountryApi");
                    var resp = await client.GetAsync(
                        $"name/{Uri.EscapeDataString(dto.Name)}?fullText=false&fields=name,cca2,flags");

                    if (resp.IsSuccessStatusCode)
                    {
                        var json = await resp.Content.ReadAsStringAsync();
                        var arr  = JsonSerializer.Deserialize<JsonElement[]>(json);
                        if (arr != null && arr.Length > 0)
                        {
                            var c = arr[0];
                            flagUrl = c.GetProperty("flags").GetProperty("svg").GetString();
                            if (string.IsNullOrWhiteSpace(code))
                                code = c.GetProperty("cca2").GetString();
                        }
                    }
                }
                catch { /* silently continue if API fails */ }
            }

            var node = new OrganizationTree
            {
                Id       = nextId,
                Name     = dto.Name,
                Code     = code,
                Label    = dto.Label,
                ParentId = dto.ParentId,
                FlagUrl  = flagUrl
            };

            _db.OrganizationTree.Add(node);
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = node.Id }, ToDto(node));
        }

        /// <summary>Update any node — name, code, label, parent, flagUrl</summary>
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] UpdateOrgNodeDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var node = await _db.OrganizationTree.FindAsync(id);
            if (node == null) return NotFound(new { message = $"Node {id} not found." });

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
            node.FlagUrl  = dto.FlagUrl;

            await _db.SaveChangesAsync();
            return Ok(ToDto(node));
        }

        /// <summary>Delete a node — blocked if it has children or vacancies</summary>
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var node = await _db.OrganizationTree.FindAsync(id);
            if (node == null) return NotFound(new { message = $"Node {id} not found." });

            if (await _db.OrganizationTree.AnyAsync(n => n.ParentId == id))
                return BadRequest(new { message = "Cannot delete — this node has children. Delete children first." });

            if (await _db.Vacancies.AnyAsync(v => v.OrganizationId == id))
                return BadRequest(new { message = "Cannot delete — this node has vacancies. Delete vacancies first." });

            _db.OrganizationTree.Remove(node);
            await _db.SaveChangesAsync();
            return Ok(new { message = $"Node '{node.Name}' (ID: {id}) deleted." });
        }

        // PRIVATE HELPERS

        private static OrgNodeDto ToDto(OrganizationTree n) => new()
        {
            Id         = n.Id,
            Name       = n.Name,
            Code       = n.Code,
            Label      = n.Label,
            ParentId   = n.ParentId,
            ParentName = n.Parent?.Name,
            FlagUrl    = n.FlagUrl
        };

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
                        FlagUrl  = n.FlagUrl,
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
                result.Add(new OrgFlatTreeDto
                {
                    Id            = n.Id,
                    Name          = n.Name,
                    Code          = n.Code,
                    Label         = n.Label,
                    ParentId      = n.ParentId,
                    Level         = level,
                    TreePath      = path,
                    TreeStructure = new string(' ', level * 3) + display,
                    FlagUrl       = n.FlagUrl
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
