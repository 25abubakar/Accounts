using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models
{
    [Table("OrganizationTree", Schema = "dbo")]
    public class OrganizationTree
    {
        [Key]
        public int Id { get; set; }

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

        // Navigation properties
        [ForeignKey("ParentId")]
        public OrganizationTree? Parent { get; set; }

        public ICollection<OrganizationTree> Children { get; set; } = new List<OrganizationTree>();
    }
}
