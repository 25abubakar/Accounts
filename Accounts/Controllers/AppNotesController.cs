using Accounts.Data;
using Accounts.DTOs.CommCenter;
using Accounts.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Accounts.Controllers
{
    /// <summary>
    /// Communication Center — notes, instructions, announcements.
    ///
    /// Access model:
    ///   • Every logged-in user can READ notes that are targeted to them.
    ///   • Every logged-in user can CREATE personal notes (forced PRIVATE / USER source).
    ///   • Admin / SuperAdmin can create instructions for any target.
    ///   • Only the creator or admin can EDIT / DELETE a note.
    ///   • Read / Acknowledge / Dismiss state is per-staff (not global).
    /// </summary>
    [ApiController]
    [Route("api/app-notes")]
    [Authorize]
    public class AppNotesController : ControllerBase
    {
        private readonly IAppNoteService      _service;
        private readonly ApplicationDbContext _db;

        public AppNotesController(IAppNoteService service, ApplicationDbContext db)
        {
            _service = service;
            _db      = db;
        }

        // ── Identity helpers ──────────────────────────────────────────────────

        private string CurrentIdentityUserId =>
            User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value
            ?? "anonymous";

        private bool IsAdmin =>
            User.IsInRole("SuperAdmin") || User.IsInRole("Admin");

        /// <summary>
        /// Resolves the StaffId (Guid as string) for the currently logged-in user.
        /// Returns null if the user has no Staff record (e.g. pure admin account).
        /// </summary>
        private async Task<string?> GetCurrentStaffIdAsync()
        {
            var identityUserId = CurrentIdentityUserId;
            if (identityUserId == "anonymous") return null;

            var person = await _db.Persons
                .AsNoTracking()
                .Include(p => p.Staff)
                .FirstOrDefaultAsync(p => p.IdentityUserId == identityUserId);

            return person?.Staff?.StaffId.ToString();
        }

        /// <summary>
        /// Returns the staffId to use for note filtering.
        /// Falls back to the identity user ID so admin-only accounts still work.
        /// </summary>
        private async Task<string> ResolveStaffIdAsync()
        {
            var staffId = await GetCurrentStaffIdAsync();
            return staffId ?? CurrentIdentityUserId;
        }

        // ── Read endpoints ────────────────────────────────────────────────────

        /// <summary>
        /// Get all notes visible to the current user.
        /// Backend filters by targets — each user only sees what is addressed to them.
        /// Frontend can then do: notes.filter(n => n.isPopup) for popups.
        /// </summary>
        [HttpGet("visible")]
        public async Task<IActionResult> GetVisible(
            [FromQuery] string? menuCode,
            [FromQuery] string? entityType,
            [FromQuery] string? entityId,
            CancellationToken ct)
        {
            var staffId = await ResolveStaffIdAsync();
            var data = await _service.GetVisibleAsync(staffId, CurrentIdentityUserId, menuCode, entityType, entityId, ct);
            return Ok(CommApiResponse<List<AppNoteDto>>.Ok(data));
        }

        /// <summary>Get a single note by ID.</summary>
        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id, CancellationToken ct)
        {
            var staffId = await ResolveStaffIdAsync();
            AppNoteDto data;
            try
            {
                data = await _service.GetByIdAsync(id, staffId, CurrentIdentityUserId, ct);
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
            return Ok(CommApiResponse<AppNoteDto>.Ok(data));
        }

        /// <summary>
        /// Unread count for the notification bell.
        /// Returns count of unread ADMIN instructions visible to this user.
        /// </summary>
        [HttpGet("unread-count")]
        public async Task<IActionResult> UnreadCount(
            [FromQuery] string? menuCode, CancellationToken ct)
        {
            var staffId = await ResolveStaffIdAsync();
            var count = await _service.GetUnreadCountAsync(staffId, CurrentIdentityUserId, menuCode, ct);
            return Ok(CommApiResponse<int>.Ok(count));
        }

        // ── Create ────────────────────────────────────────────────────────────

        /// <summary>
        /// Create a note or instruction.
        ///
        /// Admin:        can set any SourceTypeCode, VisibilityTypeCode, and targets.
        /// Regular user: SourceTypeCode is forced to "USER", VisibilityTypeCode to "PRIVATE".
        ///               Targets are ignored — the note is personal only.
        ///
        /// If no targets are provided (admin), defaults to ALL / *.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> Create(
            [FromBody] CreateAppNoteRequest request, CancellationToken ct)
        {
            if (!IsAdmin)
            {
                // Regular users can only create personal notes
                request.SourceTypeCode     = "USER";
                request.VisibilityTypeCode = "PRIVATE";
                request.Targets            = new List<AppNoteTargetRequest>();
            }

            var data = await _service.CreateAsync(request, CurrentIdentityUserId, ct);
            return Ok(CommApiResponse<AppNoteDto>.Ok(data, "Note created successfully."));
        }

        // ── Status actions ────────────────────────────────────────────────────

        /// <summary>Mark a note as read (per-staff).</summary>
        [HttpPost("{id:int}/mark-read")]
        public async Task<IActionResult> MarkRead(int id, CancellationToken ct)
        {
            var staffId = await ResolveStaffIdAsync();
            await _service.MarkReadAsync(id, staffId, ct);
            return Ok(CommApiResponse<object>.Ok(null!, "Marked as read."));
        }

        /// <summary>Acknowledge a note (per-staff).</summary>
        [HttpPost("{id:int}/acknowledge")]
        public async Task<IActionResult> Acknowledge(int id, CancellationToken ct)
        {
            var staffId = await ResolveStaffIdAsync();
            await _service.AcknowledgeAsync(id, staffId, ct);
            return Ok(CommApiResponse<object>.Ok(null!, "Acknowledged."));
        }

        /// <summary>Dismiss a note (per-staff, only if AllowDismiss = true).</summary>
        [HttpPost("{id:int}/dismiss")]
        public async Task<IActionResult> Dismiss(int id, CancellationToken ct)
        {
            var staffId = await ResolveStaffIdAsync();
            await _service.DismissAsync(id, staffId, ct);
            return Ok(CommApiResponse<object>.Ok(null!, "Dismissed."));
        }

        // ── Edit / Delete — creator or admin only ─────────────────────────────

        /// <summary>Update a note. Only the creator or an admin can edit.</summary>
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(
            int id, [FromBody] CreateAppNoteRequest request, CancellationToken ct)
        {
            var staffId  = await ResolveStaffIdAsync();
            AppNoteDto existing;
            try
            {
                existing = await _service.GetByIdAsync(id, staffId, CurrentIdentityUserId, ct);
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }

            if (!IsAdmin && existing.CreatedBy != CurrentIdentityUserId)
                return Forbid();

            var data = await _service.UpdateAsync(id, request, CurrentIdentityUserId, ct);
            return Ok(CommApiResponse<AppNoteDto>.Ok(data, "Note updated successfully."));
        }

        /// <summary>Delete a note. Only the creator or an admin can delete.</summary>
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id, CancellationToken ct)
        {
            var staffId  = await ResolveStaffIdAsync();
            AppNoteDto existing;
            try
            {
                existing = await _service.GetByIdAsync(id, staffId, CurrentIdentityUserId, ct);
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }

            if (!IsAdmin && existing.CreatedBy != CurrentIdentityUserId)
                return Forbid();

            await _service.DeleteAsync(id, CurrentIdentityUserId, ct);
            return Ok(CommApiResponse<object>.Ok(null!, "Note deleted."));
        }
    }
}
