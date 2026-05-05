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
    }

    // ── Request DTOs ──────────────────────────────────────────────

    public class CreateOrgNodeDto
    {
        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(20)]
        public string? Code { get; set; }

        /// <summary>Country / Company / Branch / Staff</summary>
        [Required]
        [MaxLength(50)]
        public string Label { get; set; } = string.Empty;

        public int? ParentId { get; set; }
    }

    public class UpdateOrgNodeDto
    {
        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(20)]
        public string? Code { get; set; }

        [Required]
        [MaxLength(50)]
        public string Label { get; set; } = string.Empty;

        public int? ParentId { get; set; }
    }
}
