using Accounts.Data;
using Accounts.Models;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services
{
    /// <summary>
    /// Generates vacancy codes in the format: {CompanyCode}-{CityCode}-{JobCode}-{NN}
    /// e.g.  LT-KHI-MGR-01,  LT-KHI-DEV-03
    /// </summary>
    public class VacancyCodeService
    {
        private readonly ApplicationDbContext _db;

        // ── Built-in job-title → abbreviation map ─────────────────────────────
        // Add / extend as needed. Keys are lower-cased for matching.
        private static readonly Dictionary<string, string> _jobCodes =
            new(StringComparer.OrdinalIgnoreCase)
            {
                // Management
                { "manager",                  "MGR"  },
                { "general manager",          "GM"   },
                { "assistant manager",        "AM"   },
                { "deputy manager",           "DM"   },
                { "senior manager",           "SM"   },
                { "head of department",       "HOD"  },
                { "head of dept",             "HOD"  },
                { "department head",          "HOD"  },
                { "director",                 "DIR"  },
                { "ceo",                      "CEO"  },
                { "coo",                      "COO"  },
                { "cfo",                      "CFO"  },
                { "cto",                      "CTO"  },
                { "chairman",                 "CHR"  },

                // Technology
                { "developer",                "DEV"  },
                { "software developer",       "DEV"  },
                { "software engineer",        "SWE"  },
                { "senior developer",         "SDEV" },
                { "junior developer",         "JDEV" },
                { "full stack developer",     "FSD"  },
                { "frontend developer",       "FED"  },
                { "backend developer",        "BED"  },
                { "mobile developer",         "MOB"  },
                { "devops engineer",          "DOP"  },
                { "qa engineer",              "QA"   },
                { "quality assurance",        "QA"   },
                { "data analyst",             "DA"   },
                { "data scientist",           "DS"   },
                { "database administrator",   "DBA"  },
                { "system administrator",     "SA"   },
                { "network engineer",         "NET"  },
                { "networking",               "NET"  },
                { "it support",               "ITS"  },
                { "cybersecurity",            "SEC"  },
                { "security engineer",        "SEC"  },
                { "ui/ux designer",           "UXD"  },
                { "ui designer",              "UID"  },
                { "ux designer",              "UXD"  },
                { "graphic designer",         "GFX"  },

                // Finance & Accounts
                { "accountant",               "ACC"  },
                { "senior accountant",        "SACC" },
                { "finance officer",          "FIN"  },
                { "finance manager",          "FM"   },
                { "auditor",                  "AUD"  },
                { "tax consultant",           "TAX"  },

                // HR
                { "hr officer",               "HRO"  },
                { "hr manager",               "HRM"  },
                { "human resources",          "HR"   },
                { "recruiter",                "REC"  },
                { "talent acquisition",       "TA"   },
                { "payroll officer",          "PAY"  },

                // Sales & Marketing
                { "marketing",                "MKT"  },
                { "marketing manager",        "MM"   },
                { "marketing officer",        "MKT"  },
                { "sales",                    "SLS"  },
                { "sales manager",            "SLM"  },
                { "sales officer",            "SLS"  },
                { "business development",     "BD"   },
                { "brand manager",            "BRM"  },
                { "digital marketing",        "DM"   },
                { "seo specialist",           "SEO"  },
                { "content writer",           "CW"   },
                { "social media manager",     "SMM"  },

                // Operations & Logistics
                { "operations manager",       "OPM"  },
                { "operations officer",       "OPS"  },
                { "logistics",                "LOG"  },
                { "supply chain",             "SCM"  },
                { "procurement officer",      "PRO"  },
                { "warehouse manager",        "WHM"  },
                { "driver",                   "DRV"  },

                // Admin & Support
                { "admin",                    "ADM"  },
                { "administrator",            "ADM"  },
                { "receptionist",             "RCP"  },
                { "office boy",               "OB"   },
                { "peon",                     "PEO"  },
                { "security guard",           "SEC"  },
                { "cleaner",                  "CLN"  },

                // Legal & Compliance
                { "legal officer",            "LEG"  },
                { "compliance officer",       "COM"  },
                { "lawyer",                   "LAW"  },

                // Customer Service
                { "customer service",         "CS"   },
                { "customer support",         "CS"   },
                { "call center agent",        "CCA"  },
                { "help desk",                "HDS"  },
            };

        public VacancyCodeService(ApplicationDbContext db) => _db = db;

        /// <summary>
        /// Generates a unique vacancy code.
        /// Format: {CompanyCode}-{CityCode}-{JobCode}-{NN}
        /// e.g.  LT-KHI-MGR-01
        /// </summary>
        public async Task<string> GenerateAsync(int organizationId, string jobTitle)
        {
            // ── 1. Walk up the org tree to find Company and City codes ─────────
            var node    = await _db.OrganizationTree.FindAsync(organizationId);
            var parent  = node?.ParentId != null ? await _db.OrganizationTree.FindAsync(node.ParentId) : null;
            var grandP  = parent?.ParentId != null ? await _db.OrganizationTree.FindAsync(parent.ParentId) : null;

            // Company code — use stored Code field, else derive from Name
            string companyCode = DeriveCode(parent ?? node, 3);

            // City code — grandparent's Code or Name abbreviation
            string cityCode = DeriveCode(grandP ?? parent ?? node, 3);

            // ── 2. Resolve job abbreviation ───────────────────────────────────
            string jobCode = ResolveJobCode(jobTitle);

            // ── 3. Build prefix and find next sequence number ─────────────────
            string prefix = $"{companyCode}-{cityCode}-{jobCode}-";

            // Count existing vacancies with same prefix to get next number
            int count = await _db.Vacancies
                .CountAsync(v => v.VacancyCode.StartsWith(prefix));

            string code;
            int seq = count + 1;

            // Ensure uniqueness (handles gaps from deletions)
            do
            {
                code = $"{prefix}{seq:D2}";
                seq++;
            }
            while (await _db.Vacancies.AnyAsync(v => v.VacancyCode == code));

            return code;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the stored Code if set, otherwise derives an abbreviation from the Name.
        /// e.g. "Lal Technology" → "LT", "Karachi" → "KHI" (if in map), else "KAR"
        /// </summary>
        private static string DeriveCode(OrganizationTree? node, int maxLen)
        {
            if (node == null) return "ORG";

            // Use stored Code field if available
            if (!string.IsNullOrWhiteSpace(node.Code))
                return node.Code.ToUpper().Trim();

            // Derive from Name — take first letter of each word
            var words = node.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string abbr = words.Length > 1
                ? string.Concat(words.Select(w => w[0])).ToUpper()   // "Lal Technology" → "LT"
                : node.Name.Length >= maxLen
                    ? node.Name[..maxLen].ToUpper()                   // "Karachi" → "KAR"
                    : node.Name.ToUpper();

            return abbr.Length > maxLen ? abbr[..maxLen] : abbr;
        }

        /// <summary>
        /// Looks up the job title in the built-in map.
        /// Falls back to first-letters of words, then first 3 chars.
        /// </summary>
        private static string ResolveJobCode(string jobTitle)
        {
            if (string.IsNullOrWhiteSpace(jobTitle)) return "POS";

            // Exact match
            if (_jobCodes.TryGetValue(jobTitle.Trim(), out var code))
                return code;

            // Partial match — check if any key is contained in the title
            var lower = jobTitle.ToLower();
            foreach (var kv in _jobCodes)
                if (lower.Contains(kv.Key.ToLower()))
                    return kv.Value;

            // Fallback — initials of words
            var words = jobTitle.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length > 1)
                return string.Concat(words.Select(w => w[0])).ToUpper();

            // Last resort — first 3 chars
            return jobTitle.Length >= 3
                ? jobTitle[..3].ToUpper()
                : jobTitle.ToUpper();
        }
    }
}
