using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models
{
    [Table("AppLookupValues")]
    public class AppLookupValue
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int LookupValueId { get; set; }

        public int LookupTypeId { get; set; }

        [Required, MaxLength(100)]
        public string ValueCode { get; set; } = string.Empty;

        [Required, MaxLength(150)]
        public string DisplayText { get; set; } = string.Empty;

        public int SortOrder { get; set; }

        public bool IsDefault { get; set; }

        public bool IsActive { get; set; } = true;

        [MaxLength(2000)]
        public string? MetadataJson { get; set; }

        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;

        [ForeignKey("LookupTypeId")]
        public AppLookupType? LookupType { get; set; }
    }
}
