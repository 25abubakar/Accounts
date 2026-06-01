using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models
{
    [Table("AppLookupTypes")]
    public class AppLookupType
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int LookupTypeId { get; set; }

        [Required, MaxLength(100)]
        public string LookupTypeCode { get; set; } = string.Empty;

        [Required, MaxLength(150)]
        public string LookupTypeName { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;

        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;

        public ICollection<AppLookupValue> Values { get; set; } = new List<AppLookupValue>();
    }
}
