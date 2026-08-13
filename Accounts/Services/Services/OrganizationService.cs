using Accounts.Data;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Accounts.Services.Services
{
    public class OrganizationService : IOrganizationService
    {
        private readonly ApplicationDbContext _db;
        private readonly IHttpClientFactory   _httpClientFactory;

        public OrganizationService(ApplicationDbContext db, IHttpClientFactory httpClientFactory)
        {
            _db                = db;
            _httpClientFactory = httpClientFactory;
        }

        // ── Country Lookup ────────────────────────────────────────────────────

        public async Task<CountryLookupDto?> CountryLookupAsync(string name)
        {
            var client   = _httpClientFactory.CreateClient("CountryApi");
            var response = await client.GetAsync(
                $"name/{Uri.EscapeDataString(name)}?fullText=false&fields=name,cca2,cca3,flags,region,capital");

            if (!response.IsSuccessStatusCode) return null;

            var json      = await response.Content.ReadAsStringAsync();
            var countries = JsonSerializer.Deserialize<JsonElement[]>(json);
            if (countries == null || countries.Length == 0) return null;

            var c = countries[0];
            return new CountryLookupDto
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
        }

        public async Task<IEnumerable<CountryLookupDto>> CountrySearchAsync(string q)
        {
            try
            {
                var client   = _httpClientFactory.CreateClient("CountryApi");
                var response = await client.GetAsync(
                    $"name/{Uri.EscapeDataString(q)}?fields=name,cca2,cca3,flags,region,capital");

                if (!response.IsSuccessStatusCode) return [];

                var json      = await response.Content.ReadAsStringAsync();
                var countries = JsonSerializer.Deserialize<JsonElement[]>(json);
                if (countries == null) return [];

                return countries.Take(10).Select(c => new CountryLookupDto
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
            }
            catch { return []; }
        }

        // ── Tree ──────────────────────────────────────────────────────────────

        public async Task<IEnumerable<OrgTreeNodeDto>> GetTreeAsync()
        {
            var all = await _db.OrganizationTree.ToListAsync();
            return BuildNestedTree(all, null, 0, "");
        }

        public async Task<IEnumerable<OrgTreeNodeDto>?> GetSubTreeAsync(int startId)
        {
            var startNode = await _db.OrganizationTree.FindAsync(startId);
            if (startNode == null) return null;

            var all = await _db.OrganizationTree.ToListAsync();
            var ids = new HashSet<int> { startId };
            CollectDescendants(all, startId, ids);

            var subtree = all.Where(n => ids.Contains(n.Id)).ToList();
            return BuildNestedTree(subtree, startNode.ParentId, 0, "");
        }

        public async Task<IEnumerable<OrgFlatTreeDto>> GetFlatTreeAsync()
        {
            var all    = await _db.OrganizationTree.ToListAsync();
            var result = new List<OrgFlatTreeDto>();
            BuildFlatTree(all, null, 0, "", result);
            return result;
        }

        // ── CRUD ──────────────────────────────────────────────────────────────

        public async Task<IEnumerable<OrgNodeDto>> GetAllAsync() =>
            await _db.OrganizationTree
                .Include(n => n.Parent)
                .OrderBy(n => n.Id)
                .Select(n => ToDto(n))
                .ToListAsync();

        public async Task<OrgNodeDto?> GetByIdAsync(int id)
        {
            var node = await _db.OrganizationTree
                .Include(n => n.Parent)
                .FirstOrDefaultAsync(n => n.Id == id);
            return node == null ? null : ToDto(node);
        }

        public async Task<IEnumerable<OrgNodeDto>> GetByLabelAsync(string label) =>
            await _db.OrganizationTree
                .Include(n => n.Parent)
                .Where(n => n.Label.ToLower() == label.ToLower())
                .Select(n => ToDto(n))
                .ToListAsync();

        public async Task<IEnumerable<OrgNodeDto>?> GetChildrenAsync(int id)
        {
            if (!await _db.OrganizationTree.AnyAsync(n => n.Id == id)) return null;
            return await _db.OrganizationTree
                .Include(n => n.Parent)
                .Where(n => n.ParentId == id)
                .Select(n => ToDto(n))
                .ToListAsync();
        }

        public async Task<IEnumerable<OrgNodeDto>> SearchAsync(string q) =>
            await _db.OrganizationTree
                .Include(n => n.Parent)
                .Where(n => n.Name.Contains(q))
                .Select(n => ToDto(n))
                .ToListAsync();

        public async Task<(OrgNodeDto Node, bool Created)> CreateAsync(CreateOrgNodeDto dto)
        {
            var nextId  = (await _db.OrganizationTree.MaxAsync(n => (int?)n.Id) ?? 0) + 1;
            string? flagUrl = dto.FlagUrl;
            string? code    = dto.Code;

            // Auto-fetch flag + code for Country nodes
            if (dto.Label.Equals("Country", StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrWhiteSpace(flagUrl))
            {
                try
                {
                    var client = _httpClientFactory.CreateClient("CountryApi");
                    var resp   = await client.GetAsync(
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
                catch { /* silently continue */ }
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
            return (ToDto(node), true);
        }

        public async Task<OrgNodeDto?> UpdateAsync(int id, UpdateOrgNodeDto dto)
        {
            var node = await _db.OrganizationTree.FindAsync(id);
            if (node == null) return null;

            node.Name     = dto.Name;
            node.Code     = dto.Code;
            node.Label    = dto.Label;
            node.ParentId = dto.ParentId;
            node.FlagUrl  = dto.FlagUrl;

            await _db.SaveChangesAsync();
            return ToDto(node);
        }

        public async Task<(bool Success, string Message)> DeleteAsync(int id)
        {
            var node = await _db.OrganizationTree.FindAsync(id);
            if (node == null) return (false, $"Node {id} not found.");

            if (await _db.OrganizationTree.AnyAsync(n => n.ParentId == id))
                return (false, "Cannot delete — this node has children. Delete children first.");

            if (await _db.Vacancies.AnyAsync(v => v.OrganizationId == id))
                return (false, "Cannot delete — this node has vacancies. Delete vacancies first.");

            var tenantName = await _db.Tenants
                .Where(t => t.OrganizationTreeId == id)
                .Select(t => t.TenantName)
                .FirstOrDefaultAsync();
            if (tenantName != null)
                return (false, $"Cannot delete — this node is assigned to tenant '{tenantName}'. Remove or reassign the tenant first.");

            if (await _db.ChatWorkspaces.AnyAsync(w => w.OrganizationTreeId == id))
                return (false, "Cannot delete — this node has an active chat workspace. Remove the tenant first.");

            _db.OrganizationTree.Remove(node);
            await _db.SaveChangesAsync();
            return (true, $"Node '{node.Name}' (ID: {id}) deleted.");
        }

        // ── Private Helpers ───────────────────────────────────────────────────

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
            return all.Where(n => n.ParentId == parentId).OrderBy(n => n.Id)
                .Select(n =>
                {
                    var display = (n.Code != null ? $"[{n.Code}] " : "") + n.Name;
                    var path    = string.IsNullOrEmpty(parentPath) ? display : $"{parentPath} → {display}";
                    return new OrgTreeNodeDto
                    {
                        Id = n.Id, Name = n.Name, Code = n.Code, Label = n.Label,
                        ParentId = n.ParentId, Level = level, TreePath = path, FlagUrl = n.FlagUrl,
                        Children = BuildNestedTree(all, n.Id, level + 1, path)
                    };
                }).ToList();
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
                    Id = n.Id, Name = n.Name, Code = n.Code, Label = n.Label,
                    ParentId = n.ParentId, Level = level, TreePath = path,
                    TreeStructure = new string(' ', level * 3) + display, FlagUrl = n.FlagUrl
                });
                BuildFlatTree(all, n.Id, level + 1, path, result);
            }
        }

        private static void CollectDescendants(List<OrganizationTree> all, int parentId, HashSet<int> ids)
        {
            foreach (var child in all.Where(n => n.ParentId == parentId))
            {
                ids.Add(child.Id);
                CollectDescendants(all, child.Id, ids);
            }
        }
    }
}
