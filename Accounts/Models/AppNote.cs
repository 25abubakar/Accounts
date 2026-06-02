using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models
{
    [Table("AppNotes")]
    public class AppNote
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int NoteId { get; set; }

        public int? TenantId { get; set; }
        public int? OrgUnitId { get; set; }

        [Required, MaxLength(250)]
        public string Title { get; set; } = string.Empty;

        [Required]
        public string NoteBody { get; set; } = string.Empty;

        [Required, MaxLength(100)]
        public string NoteTypeCode { get; set; } = string.Empty;

        [Required, MaxLength(100)]
        public string SourceTypeCode { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? CategoryCode { get; set; }

        [Required, MaxLength(100)]
        public string PriorityCode { get; set; } = string.Empty;

        [Required, MaxLength(100)]
        public string VisibilityTypeCode { get; set; } = string.Empty;

        [MaxLength(150)]
        public string? MenuCode { get; set; }

        [MaxLength(150)]
        public string? ModuleName { get; set; }

        [MaxLength(100)]
        public string? EntityType { get; set; }

        [MaxLength(100)]
        public string? EntityId { get; set; }

        public DateTime? StartDateUtc { get; set; }
        public DateTime? EndDateUtc { get; set; }

        public bool IsPublished { get; set; } = true;
        public bool IsPinned { get; set; }
        public bool IsPopup { get; set; }
        public bool RequireAcknowledgement { get; set; }
        public bool AllowDismiss { get; set; } = true;
        public bool IsActive { get; set; } = true;
        public bool IsDeleted { get; set; }

        [MaxLength(100)]
        public string? CreatedBy { get; set; }
        public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;

        [MaxLength(450)]
        public string? OwnerIdentityUserId { get; set; }

        [MaxLength(100)]
        public string? UpdatedBy { get; set; }
        public DateTime? UpdatedOnUtc { get; set; }

        [MaxLength(100)]
        public string? DeletedBy { get; set; }
        public DateTime? DeletedOnUtc { get; set; }

        // Navigation
        public ICollection<AppNoteTarget>     Targets      { get; set; } = new List<AppNoteTarget>();
        public ICollection<AppNoteUserStatus> UserStatuses { get; set; } = new List<AppNoteUserStatus>();
        public ICollection<AppNoteUserState>  UserStates   { get; set; } = new List<AppNoteUserState>();
        public ICollection<AppNoteAttachment> Attachments  { get; set; } = new List<AppNoteAttachment>();
    }
}
