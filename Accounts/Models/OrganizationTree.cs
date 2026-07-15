using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models
{
    [Table("OrganizationTree", Schema = "dbo")]
    public class OrganizationTree
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(20)]
        public string? Code { get; set; }

        /// <summary>
        /// Flexible label — any value: Country, Group, Company, Division,
        /// Region, Branch, Department, Team, Staff, etc.
        /// </summary>
        [Required]
        [MaxLength(50)]
        public string Label { get; set; } = string.Empty;

        public int? ParentId { get; set; }

        /// <summary>
        /// Controls whether this organization node can be used for access. A disabled
        /// node also blocks users whose tenant belongs to any of its descendants.
        /// </summary>
        public bool IsActive { get; set; } = true;

        /// <summary>Flag image URL — auto-fetched for Country nodes</summary>
        [MaxLength(500)]
        public string? FlagUrl { get; set; }

        [ForeignKey("ParentId")]
        public OrganizationTree? Parent { get; set; }

        public ICollection<OrganizationTree> Children { get; set; } = new List<OrganizationTree>();
    }
}
