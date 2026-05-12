using Accounts.Data;
using Accounts.Models;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services
{
    /// <summary>
    /// Generates vacancy codes.
    ///
    /// NEW FORMAT (as per business requirement):
    ///   {Country}-{Group}-{CompanyInitials}-{AutoIncrementNumber}
    ///
    /// Examples:
    ///   Pakistan-LalGroup-LT-1
    ///   Pakistan-LalGroup-LT-2
    ///   Pakistan-LalGroup-NS-1
    ///
    /// Rules:
    ///   Country      = Country node Name  (e.g. "Pakistan")
    ///   Group        = Group node Name    (e.g. "LalGroup") — spaces removed
    ///   Company      = Company initials   (e.g. "LT" for "Lal Technology")
    ///   AutoIncrement= Global counter per Country-Group-Company prefix, starts at 1
    /// </summary>
    public class VacancyCodeService
    {
        private readonly ApplicationDbContext _db;

        public VacancyCodeService(ApplicationDbContext db) => _db = db;

        /// <summary>
        /// Generates a unique vacancy code.
        /// Walks up the org tree from the given node to find Country, Group, Company.
        /// Format: {Country}-{Group}-{CompanyInitials}-{N}
        /// </summary>
        public async Task<string> GenerateAsync(int organizationId, string jobTitle)
        {
            // ── 1. Load the full ancestor chain ──────────────────────────────
            var chain = await BuildAncestorChainAsync(organizationId);

            // ── 2. Resolve Country, Group, Company from the chain ─────────────
            var country = FindByLabel(chain, "Country");
            var group   = FindByLabel(chain, "Group");
            var company = FindByLabel(chain, "Company");

            // Fallback: if labels don't match exactly, use positional order
            // chain[0] = deepest node (the org node itself)
            // chain[last] = root (Country)
            if (country == null && chain.Count > 0)
                country = chain.Last();   // root = Country

            if (company == null && chain.Count >= 2)
                company = chain[^2];      // one level above root = Company (or Group)

            if (group == null && chain.Count >= 3)
                group = chain[^3];        // two levels above root = Group

            // ── 3. Build each segment ─────────────────────────────────────────
            string countryPart  = SanitizeName(country?.Name ?? "ORG");
            string groupPart    = SanitizeName(group?.Name   ?? "GRP");
            string companyPart  = GetInitials(company ?? chain.FirstOrDefault());

            // ── 4. Build prefix and find next sequence number ─────────────────
            // Format: Pakistan-LalGroup-LT-
            string prefix = $"{countryPart}-{groupPart}-{companyPart}-";

            int count = await _db.Vacancies
                .CountAsync(v => v.VacancyCode.StartsWith(prefix));

            string code;
            int seq = count + 1;   // starts at 1, increments by 1

            // Guarantee uniqueness even if there are gaps from deletions
            do
            {
                code = $"{prefix}{seq}";
                seq++;
            }
            while (await _db.Vacancies.AnyAsync(v => v.VacancyCode == code));

            return code;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Walks up the org tree from the given node and returns the full ancestor chain.
        /// Index 0 = the node itself, last index = root (Country).
        /// </summary>
        private async Task<List<OrganizationTree>> BuildAncestorChainAsync(int nodeId)
        {
            var chain = new List<OrganizationTree>();
            int? currentId = nodeId;

            while (currentId.HasValue)
            {
                var node = await _db.OrganizationTree.FindAsync(currentId.Value);
                if (node == null) break;
                chain.Add(node);
                currentId = node.ParentId;
            }

            return chain; // [0]=deepest, [last]=root
        }

        /// <summary>Find a node in the chain by its Label (case-insensitive)</summary>
        private static OrganizationTree? FindByLabel(List<OrganizationTree> chain, string label) =>
            chain.FirstOrDefault(n => string.Equals(n.Label, label, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Gets company initials — uses stored Code if available,
        /// otherwise takes first letter of each word.
        /// "Lal Technology" → "LT", "NetSolutions" → "NS"
        /// </summary>
        private static string GetInitials(OrganizationTree? node)
        {
            if (node == null) return "CO";
            if (!string.IsNullOrWhiteSpace(node.Code)) return node.Code.ToUpper().Trim();

            var words = node.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length >= 2)
                return string.Concat(words.Select(w => char.ToUpper(w[0])));

            return node.Name.Length >= 2
                ? node.Name[..Math.Min(3, node.Name.Length)].ToUpper()
                : node.Name.ToUpper();
        }

        /// <summary>
        /// Removes spaces and special chars from a name for use in a code.
        /// "Lal Group" → "LalGroup", "Pakistan" → "Pakistan"
        /// </summary>
        private static string SanitizeName(string name) =>
            string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                              .Select(w => char.ToUpper(w[0]) + w[1..]));
    }
}
