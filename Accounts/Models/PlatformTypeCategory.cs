using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models;

[Table("PlatformTypeCategories")]
public sealed class PlatformTypeCategory
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string Code { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(50)]
    public string Icon { get; set; } = "Shapes";

    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<PlatformTypeValue> Values { get; set; } = new List<PlatformTypeValue>();
}
