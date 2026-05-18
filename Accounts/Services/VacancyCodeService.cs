using Accounts.Data;
using Accounts.Models;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services
{
    /// <summary>
    /// Generates vacancy codes in the format:
    ///
    ///   {DeptAbbr}-{CompanyAbbr}-{JobTitleAbbr}-{NN}
    ///
    /// Rules:
    ///   • DeptAbbr      = abbreviation of the selected org node (the deepest node you pick)
    ///   • CompanyAbbr   = abbreviation of the nearest ancestor whose Label == "Company"
    ///                     (falls back to the node 2 levels up, then the node itself)
    ///   • JobTitleAbbr  = first 3 letters of each word in the job title, joined with nothing
    ///                     "Supervisor"         → "sup"
    ///                     "Software Engineer"  → "sof-eng"  (each word abbreviated)
    ///   • NN            = zero-padded 2-digit sequence per prefix (01, 02 … 99, 100, …)
    ///
    /// Example:
    ///   Node  : Software Dept  (Label = Department, under Lal Technology Company)
    ///   Title : Supervisor
    ///   Code  : sof-lt-sup-01
    ///
    ///   Node  : Software Dept  (Label = Department, under Lal Technology Company)
    ///   Title : Software Engineer
    ///   Code  : sof-lt-sof-eng-01
    ///
    /// Counter table (VacancyCounters) stores last-used number per prefix.
    /// SQL UPDLOCK ensures no duplicate numbers under concurrent requests.
    /// </summary>
    public class VacancyCodeService
    {
        private readonly ApplicationDbContext _db;

        public VacancyCodeService(ApplicationDbContext db) => _db = db;

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Generates the next unique vacancy code.
        /// INCREMENTS the counter — call only when actually creating a vacancy.
        /// </summary>
        public async Task<string> GenerateAsync(int organizationId, string jobTitle)
        {
            var prefix = await BuildPrefixAsync(organizationId, jobTitle);
            int next   = await GetNextNumberAsync(prefix);
            return $"{prefix}{next:D2}";
        }

        /// <summary>
        /// Previews what the next code WOULD be — does NOT increment the counter.
        /// Safe to call repeatedly from the frontend.
        /// </summary>
        public async Task<string> PreviewAsync(int organizationId, string jobTitle)
        {
            var prefix = await BuildPrefixAsync(organizationId, jobTitle);

            var counter = await _db.VacancyCounters
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Prefix == prefix);

            int next = (counter?.LastNumber ?? 0) + 1;
            return $"{prefix}{next:D2}";
        }

        // ── Prefix Builder ────────────────────────────────────────────────────

        /// <summary>
        /// Builds the prefix:  {deptAbbr}-{companyAbbr}-{jobAbbr}-
        ///
        /// Chain (index 0 = selected node, last = root):
        ///   [0] Software Dept  (Department)
        ///   [1] Lal Technology (Company)
        ///   [2] Lal Group      (Group)
        ///   [3] Pakistan       (Country)
        ///
        /// DeptAbbr    = abbreviate(chain[0])   → "sof"
        /// CompanyAbbr = abbreviate(Company node) → "lt"
        /// JobAbbr     = abbreviate each word of jobTitle → "sup"
        /// </summary>
        private async Task<string> BuildPrefixAsync(int organizationId, string jobTitle)
        {
            var chain = await BuildAncestorChainAsync(organizationId);
            if (chain.Count == 0)
                throw new InvalidOperationException($"Organization node {organizationId} not found.");

            // The node the user selected (deepest — Department, Branch, etc.)
            var selectedNode = chain[0];

            // Find Company by label; fall back to the node 1 level up, then the node itself
            var companyNode = FindByLabel(chain, "Company")
                           ?? (chain.Count >= 2 ? chain[1] : chain[0]);

            string deptPart    = Abbreviate(selectedNode.Name);
            string companyPart = GetInitials(companyNode);
            string jobPart     = AbbreviateJobTitle(jobTitle);

            // e.g.  "sof-lt-sup-"
            return $"{deptPart}-{companyPart}-{jobPart}-";
        }

        // ── Atomic Counter ────────────────────────────────────────────────────

        private async Task<int> GetNextNumberAsync(string prefix)
        {
            // Wrap in execution strategy so it works with SqlServerRetryingExecutionStrategy
            var strategy = _db.Database.CreateExecutionStrategy();

            return await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await _db.Database.BeginTransactionAsync();
                try
                {
                    var counter = await _db.VacancyCounters
                        .FromSqlRaw(
                            "SELECT * FROM VacancyCounters WITH (UPDLOCK, ROWLOCK) WHERE Prefix = {0}",
                            prefix)
                        .FirstOrDefaultAsync();

                    int next;
                    if (counter == null)
                    {
                        counter = new VacancyCounter { Prefix = prefix, LastNumber = 1 };
                        _db.VacancyCounters.Add(counter);
                        next = 1;
                    }
                    else
                    {
                        counter.LastNumber += 1;
                        next = counter.LastNumber;
                    }

                    await _db.SaveChangesAsync();
                    await tx.CommitAsync();
                    return next;
                }
                catch
                {
                    await tx.RollbackAsync();
                    throw;
                }
            });
        }

        // ── Org Tree Helpers ──────────────────────────────────────────────────

        /// <summary>
        /// Walks up the tree from nodeId.
        /// chain[0] = selected node (deepest), chain[last] = root.
        /// </summary>
        private async Task<List<OrganizationTree>> BuildAncestorChainAsync(int nodeId)
        {
            var chain = new List<OrganizationTree>();
            int? current = nodeId;

            while (current.HasValue)
            {
                var node = await _db.OrganizationTree.FindAsync(current.Value);
                if (node == null) break;
                chain.Add(node);
                current = node.ParentId;
            }

            return chain;
        }

        private static OrganizationTree? FindByLabel(List<OrganizationTree> chain, string label) =>
            chain.FirstOrDefault(n => string.Equals(n.Label, label, StringComparison.OrdinalIgnoreCase));

        // ── Abbreviation Helpers ──────────────────────────────────────────────

        /// <summary>
        /// Abbreviates a node name to 3 lowercase letters per word, joined by nothing.
        /// "Software Dept"   → "sof"   (first word only if single meaningful word)
        /// "Software Dept"   → "sofdep" if you want both — but we take first word only
        ///
        /// Rule: take first 3 chars of the FIRST word (lowercase).
        /// If the name has multiple words, take first 3 chars of EACH word joined.
        /// "Software Dept"   → "sofdep"
        /// "Lal Technology"  → "lalte"  — but for dept we just use first word → "sof"
        ///
        /// Actually: take first 3 chars of each word, join with nothing, lowercase.
        /// "Software Dept"   → "sofdep"
        /// "Software"        → "sof"
        /// "IT"              → "it"
        /// </summary>
        private static string Abbreviate(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "org";

            var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return string.Concat(words.Select(w => w.Length >= 3 ? w[..3].ToLower() : w.ToLower()));
        }

        /// <summary>
        /// Gets company initials — uses stored Code if available,
        /// otherwise takes first letter of each word (uppercase → lowercase).
        /// "Lal Technology"  → "lt"
        /// "NetSolutions"    → "ns"
        /// </summary>
        private static string GetInitials(OrganizationTree? node)
        {
            if (node == null) return "co";

            // Use explicit Code field if set
            if (!string.IsNullOrWhiteSpace(node.Code))
                return node.Code.ToLower().Trim();

            // Space-separated words → first letter of each
            var spaceWords = node.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (spaceWords.Length >= 2)
                return string.Concat(spaceWords.Select(w => char.ToLower(w[0]))).ToLower();

            // CamelCase split
            var camelWords = SplitCamelCase(node.Name);
            if (camelWords.Length >= 2)
                return string.Concat(camelWords.Select(w => char.ToLower(w[0]))).ToLower();

            // Single word — first 2 chars
            return node.Name.Length >= 2 ? node.Name[..2].ToLower() : node.Name.ToLower();
        }

        /// <summary>
        /// Abbreviates a job title: first 3 chars of each word, joined by "-", lowercase.
        /// "Supervisor"        → "sup"
        /// "Software Engineer" → "sof-eng"
        /// "HR Officer"        → "hr-off"
        /// "CEO"               → "ceo"
        /// </summary>
        private static string AbbreviateJobTitle(string jobTitle)
        {
            if (string.IsNullOrWhiteSpace(jobTitle)) return "pos";

            var words = jobTitle.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var parts = words.Select(w => w.Length >= 3 ? w[..3].ToLower() : w.ToLower());
            return string.Join("-", parts);
        }

        /// <summary>Splits CamelCase into words. "NetSolutions" → ["Net","Solutions"]</summary>
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
