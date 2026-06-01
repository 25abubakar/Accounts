using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models
{
    [Table("AppMenuDefinitions")]
    public class AppMenuDefinition
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int MenuDefinitionId { get; set; }

        [Required, MaxLength(150)]
        public string MenuCode { get; set; } = string.Empty;

        [Required, MaxLength(200)]
        public string MenuName { get; set; } = string.Empty;

        [MaxLength(150)]
        public string? ModuleName { get; set; }

        [MaxLength(150)]
        public string? ParentMenuCode { get; set; }

        [MaxLength(300)]
        public string? RoutePath { get; set; }

        [MaxLength(100)]
        public string? IconCss { get; set; }

        public int SortOrder { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
    }
}
