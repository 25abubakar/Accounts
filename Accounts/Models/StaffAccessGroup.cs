using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models
{

    [Table("StaffAccessGroups")]
    public class StaffAccessGroup
    {
        public Guid StaffId { get; set; }

        public int GroupId { get; set; }

        [MaxLength(450)]
        public string? AssignedBy { get; set; }

        public DateTime AssignedDate { get; set; } = DateTime.UtcNow;

        [MaxLength(250)]
        public string? Note { get; set; }

        [ForeignKey("StaffId")]
        public Staff? Staff { get; set; }

        [ForeignKey("GroupId")]
        public AccessGroup? Group { get; set; }
    }
}
