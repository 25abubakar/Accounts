using Accounts.DTOs.CommCenter;

namespace Accounts.Models
{
    /// <summary>
    /// Post-login bootstrap payload: sidebar, permissions, and admin instructions.
    /// </summary>
    public class UserSessionDto
    {
        public bool IsFullAccess { get; set; }
        public Guid? PersonId { get; set; }
        public Guid? StaffId { get; set; }
        public string? IdentityUserId { get; set; }

        // ── Multi-tenant fields ───────────────────────────────────────────────
        public int?  TenantId      { get; set; }
        public bool  IsSuperAdmin  { get; set; }
        public bool  IsTenantAdmin { get; set; }

        public List<object> Sidebar { get; set; } = new();
        public List<string> Permissions { get; set; } = new();
        public List<AppNoteDto> LoginInstructions { get; set; } = new();
        public int UnreadInstructionCount { get; set; }
    }

    public class GrantMenuAccessDto
    {
        public string? Reason { get; set; }
    }

    public class AdminInstructionDto : AppNoteDto
    {
        public List<AppNoteTargetRequest> Targets { get; set; } = new();
        public DateTime? StartDateUtc { get; set; }
        public DateTime? EndDateUtc { get; set; }
        public bool IsActive { get; set; }
    }
}
