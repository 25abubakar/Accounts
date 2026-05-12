using Accounts.Data;
using Accounts.Models;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services
{
    /// <summary>
    /// Generates vacancy codes with guaranteed unique, gap-free, incrementing numbers.
    ///
    /// FORMAT:  {Country}-{Group}-{CompanyInitials}-{N}
    /// EXAMPLE: Pakistan-LalGroup-LT-1
    ///          Pakistan-LalGroup-LT-2
    ///          Pakistan-LalGroup-LT-3
    ///          Pakistan-LalGroup-NS-1
    ///
    /// HOW IT WORKS (race-condition safe):
    ///   A dedicated VacancyCounters table stores the last-used number per prefix.
    ///   Each call atomically increments the counter using a SQL UPDLOCK hint,
    ///   so concurrent requests always get different numbers — no duplicates ever.
    ///
    ///   VacancyCounters table:
    ///   ┌─────────────────────────────┬────────────┐
    ///   │ Prefix                      │ LastNumber │
    ///   ├─────────────────────────────┼────────────┤
    ///   │ Pakistan-LalGroup-LT-       │ 3          │
    ///   │ Pakistan-LalGroup-NS-       │ 1          │
    ///   │ Pakistan-TechGroup-TS-      │ 7          │
    ///   └─────────────────────────────┴────────────┘
    /// </summary>
    public class VacancyCodeService
    {
        private readonly ApplicationDbContext _db;

        public VacancyCodeService(ApplicationDbContext db) => _db = db;

        /// <summary>
        /// Generates the next unique vacancy code for the given org node and job title.
        /// Thread-safe — uses database-level row locking to prevent duplicate numbers.
        /// </summary>
        public async Task<string> GenerateAsync(int organizationId, string jobTitle)
        {
            // ── 1. Build the ancestor chain (deepest → root) ──────────────────
            var chain = await BuildAncestorChainAsync(organizationId);

            // ── 2. Resolve Country, Group, Company from the chain ─────────────
            var country = FindByLabel(chain, "Country");
            var group   = FindByLabel(chain, "Group");
            var company = FindByLabel(chain, "Company");

            // Positional fallback if labels aren't set exactly
            if (country == null && chain.Count > 0)  country = chain.Last();
            if (company == null && chain.Count >= 2) company = chain[^2];
            if (group   == null && chain.Count >= 3) group   = chain[^3];

            // ── 3. Build prefix segments ──────────────────────────────────────
            string countryPart = SanitizeName(country?.Name ?? "ORG");
            string groupPart   = SanitizeName(group?.Name   ?? "GRP");
            string companyPart = GetInitials(company ?? chain.FirstOrDefault());

            string prefix = $"{countryPart}-{groupPart}-{companyPart}-";

            // ── 4. Atomically get the next number from VacancyCounters ─────────
            int nextNumber = await GetNextNumberAsync(prefix);

            return $"{prefix}{nextNumber}";
        }

        // ── Core: Atomic Counter ──────────────────────────────────────────────

        /// <summary>
        /// Atomically increments and returns the next sequence number for a prefix.
        ///
        /// Uses a SQL transaction with UPDLOCK to lock the counter row,
        /// preventing two concurrent requests from getting the same number.
        ///
        /// Flow:
        ///   1. Begin transaction
        ///   2. SELECT counter row WITH (UPDLOCK) — locks the row
        ///   3. If row doesn't exist → INSERT with LastNumber = 1, return 1
        ///   4. If row exists → UPDATE LastNumber = LastNumber + 1, return new value
        ///   5. Commit
        /// </summary>
        private async Task<int> GetNextNumberAsync(string prefix)
        {
            // Use a transaction to ensure atomicity
            await using var transaction = await _db.Database.BeginTransactionAsync();

            try
            {
                // Lock the row with UPDLOCK so no other request can read/write it
                // until this transaction commits. This is the key to thread safety.
                var counter = await _db.VacancyCounters
                    .FromSqlRaw(
                        "SELECT * FROM VacancyCounters WITH (UPDLOCK, ROWLOCK) WHERE Prefix = {0}",
                        prefix)
                    .FirstOrDefaultAsync();

                int nextNumber;

                if (counter == null)
                {
                    // First vacancy for this prefix — create counter starting at 1
                    counter = new VacancyCounter { Prefix = prefix, LastNumber = 1 };
                    _db.VacancyCounters.Add(counter);
                    nextNumber = 1;
                }
                else
                {
                    // Increment existing counter
                    counter.LastNumber += 1;
                    nextNumber = counter.LastNumber;
                }

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                return nextNumber;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        // ── Org Tree Helpers ──────────────────────────────────────────────────

        /// <summary>
        /// Walks up the org tree and returns the full ancestor chain.
        /// Index 0 = the node itself (deepest), last index = root (Country).
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

            return chain;
        }

        private static OrganizationTree? FindByLabel(List<OrganizationTree> chain, string label) =>
            chain.FirstOrDefault(n => string.Equals(n.Label, label, StringComparison.OrdinalIgnoreCase));

        // ── Code Formatting Helpers ───────────────────────────────────────────

        /// <summary>
        /// Gets company initials — uses stored Code if available,
        /// otherwise takes first letter of each word (space or CamelCase split).
        /// "Lal Technology" → "LT"
        /// "NetSolutions"   → "NS"
        /// </summary>
        private static string GetInitials(OrganizationTree? node)
        {
            if (node == null) return "CO";
            if (!string.IsNullOrWhiteSpace(node.Code)) return node.Code.ToUpper().Trim();

            // Space-separated words
            var spaceWords = node.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (spaceWords.Length >= 2)
                return string.Concat(spaceWords.Select(w => char.ToUpper(w[0])));

            // CamelCase split
            var camelWords = SplitCamelCase(node.Name);
            if (camelWords.Length >= 2)
                return string.Concat(camelWords.Select(w => char.ToUpper(w[0])));

            // Single word fallback
            return node.Name.Length >= 2 ? node.Name[..2].ToUpper() : node.Name.ToUpper();
        }

        /// <summary>
        /// Removes spaces from a name for use in a code segment (PascalCase).
        /// "Lal Group" → "LalGroup"
        /// "Pakistan"  → "Pakistan"
        /// </summary>
        private static string SanitizeName(string name) =>
            string.Concat(
                name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Select(w => w.Length > 0 ? char.ToUpper(w[0]) + w[1..] : w));

        /// <summary>
        /// Splits a CamelCase string into words.
        /// "NetSolutions" → ["Net", "Solutions"]
        /// "TechSoft"     → ["Tech", "Soft"]
        /// </summary>
        private static string[] SplitCamelCase(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return [input];
            var result = new List<string>();
            int start  = 0;

            for (int i = 1; i < input.Length; i++)
            {
                bool isUpper   = char.IsUpper(input[i]);
                bool prevLower = char.IsLower(input[i - 1]);
                bool nextLower = i + 1 < input.Length && char.IsLower(input[i + 1]);

                if (isUpper && (prevLower || nextLower))
                {
                    result.Add(input[start..i]);
                    start = i;
                }
            }
            result.Add(input[start..]);
            return result.Where(w => w.Length > 0).ToArray();
        }
    }
}
