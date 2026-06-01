using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models
{
    [Table("AppNoteUserStatuses")]
    public class AppNoteUserStatus
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int NoteUserStatusId { get; set; }

        public int NoteId { get; set; }

        [Required, MaxLength(100)]
        public string UserId { get; set; } = string.Empty;

        public bool IsRead { get; set; }
        public DateTime? ReadOnUtc { get; set; }

        public bool IsAcknowledged { get; set; }
        public DateTime? AcknowledgedOnUtc { get; set; }

        public bool IsDismissed { get; set; }
        public DateTime? DismissedOnUtc { get; set; }

        public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedOnUtc { get; set; }

        [ForeignKey("NoteId")]
        public AppNote? Note { get; set; }
    }
}
