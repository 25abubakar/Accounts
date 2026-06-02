using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models
{
    /// <summary>
    /// Per-staff read / acknowledge / dismiss state for a note.
    /// One row per (NoteId, StaffId) pair.
    /// </summary>
    [Table("AppNoteUserStates")]
    public class AppNoteUserState
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int AppNoteUserStateId { get; set; }

        public int NoteId { get; set; }

        /// <summary>StaffId as string (Guid.ToString())</summary>
        [Required, MaxLength(100)]
        public string StaffId { get; set; } = string.Empty;

        public bool IsRead          { get; set; }
        public bool IsAcknowledged  { get; set; }
        public bool IsDismissed     { get; set; }

        public DateTime? ReadOnUtc          { get; set; }
        public DateTime? AcknowledgedOnUtc  { get; set; }
        public DateTime? DismissedOnUtc     { get; set; }

        [ForeignKey("NoteId")]
        public AppNote? Note { get; set; }
    }
}
