using System.ComponentModel.DataAnnotations;

namespace Accounts.Models
{
    // ── Response DTOs ─────────────────────────────────────────────

    public class OrgNodeDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Code { get; set; }
        public string Label { get; set; } = string.Empty;
        public int? ParentId { get; set; }
        public string? ParentName { get; set; }
        public string? FlagUrl { get; set; }
    }

    public class OrgTreeNodeDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Code { get; set; }
        public string Label { get; set; } = string.Empty;
        public int? ParentId { get; set; }
        public int Level { get; set; }
        public string TreePath { get; set; } = string.Empty;
        public string? FlagUrl { get; set; }
        public List<OrgTreeNodeDto> Children { get; set; } = new();
    }

    public class OrgFlatTreeDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Code { get; set; }
        public string Label { get; set; } = string.Empty;
        public int? ParentId { get; set; }
        public int Level { get; set; }
        public string TreePath { get; set; } = string.Empty;
        public string TreeStructure { get; set; } = string.Empty;
        public string? FlagUrl { get; set; }
    }

    // ── Request DTOs ──────────────────────────────────────────────

    public class CreateOrgNodeDto
    {
        [Required, MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(20)]
        public string? Code { get; set; }

        /// <summary>
        /// Any label: Country, Group, Company, Division, Region,
        /// Branch, Department, Team, Staff, etc.
        /// </summary>
        [Required, MaxLength(50)]
        public string Label { get; set; } = string.Empty;

        public int? ParentId { get; set; }

        /// <summary>Optional — auto-fetched for Country nodes if not provided</summary>
        [MaxLength(500)]
        public string? FlagUrl { get; set; }
    }

    public class UpdateOrgNodeDto
    {
        [Required, MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(20)]
        public string? Code { get; set; }

        [Required, MaxLength(50)]
        public string Label { get; set; } = string.Empty;

        public int? ParentId { get; set; }

        [MaxLength(500)]
        public string? FlagUrl { get; set; }
    }

    // ── Country Lookup Response ───────────────────────────────────

    public class CountryLookupDto
    {
        public string Name { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;       // ISO 2-letter: PK
        public string Code3 { get; set; } = string.Empty;      // ISO 3-letter: PAK
        public string FlagUrl { get; set; } = string.Empty;    // SVG flag URL
        public string FlagPng { get; set; } = string.Empty;    // PNG flag URL
        public string Region { get; set; } = string.Empty;
        public string Capital { get; set; } = string.Empty;
    }
}
