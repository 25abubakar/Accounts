using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models
{

    [Table("AccessGroups")]
    public class AccessGroup
    {
        [Key]
        public int GroupId { get; set; }

        [Required, MaxLength(100)]
        public string GroupName { get; set; } = string.Empty;

        [MaxLength(250)]
        public string? Description { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

        public ICollection<AccessGroupFeature>  Features { get; set; } = new List<AccessGroupFeature>();
        public ICollection<StaffAccessGroup>    Staff    { get; set; } = new List<StaffAccessGroup>();
    }
}
