using Accounts.Data;
using Accounts.Authorization;
using Accounts.DTOs.CommCenter;
using Accounts.Models;
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

        private async Task<string?> ResolveIdentityUserIdAsync()
        {
            var idFromClaims = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                               ?? User.FindFirst("sub")?.Value;

            // Authentication plus AccountScopeAccessMiddleware already validate
            // this identity on every request, so re-querying AspNetUsers here
            // added no protection and multiplied note-polling database calls.
            if (!string.IsNullOrWhiteSpace(idFromClaims))
                return idFromClaims;

            // Fallback 1: map by username claim
            var userName = User.FindFirst(ClaimTypes.Name)?.Value
                           ?? User.Identity?.Name;
            if (!string.IsNullOrWhiteSpace(userName))
            {
                var byUserName = await _db.Users.AsNoTracking()
                    .Where(u => u.UserName == userName)
                    .Select(u => u.Id)
                    .FirstOrDefaultAsync();
                if (!string.IsNullOrWhiteSpace(byUserName))
                    return byUserName;
            }

            // Fallback 2: map by email claim
            var email = User.FindFirst(ClaimTypes.Email)?.Value;
            if (!string.IsNullOrWhiteSpace(email))
            {
                var byEmail = await _db.Users.AsNoTracking()
                    .Where(u => u.Email == email)
                    .Select(u => u.Id)
                    .FirstOrDefaultAsync();
                if (!string.IsNullOrWhiteSpace(byEmail))
                    return byEmail;
            }

            return null;
        }

        private bool IsAdmin =>
            User.IsInRole("SuperAdmin") ||
            User.IsInRole("Admin") ||
            User.IsInRole("TenantAdmin") ||
            string.Equals(User.FindFirstValue(ITenantService.ClaimIsTenantAdmin), "true", StringComparison.OrdinalIgnoreCase);

        private async Task<bool> HasInstructionPermissionAsync(string action, CancellationToken ct)
        {
            if (IsAdmin) return true;

            var normalizedAction = action.Trim().ToUpperInvariant();
            if (normalizedAction is not ("VIEW" or "ADD" or "EDIT" or "DELETE"))
                return false;

            var staffId = await GetCurrentStaffIdAsync();
            if (!Guid.TryParse(staffId, out var staffGuid))
                return false;

            return await _db.StaffMenuAccesses.AsNoTracking()
                .Where(access => access.StaffId == staffGuid && access.IsAllow)
                .Where(access => access.Menu != null &&
                    (access.Menu.Route == "/settings/instruction" || access.Menu.Route == "/instructions"))
                .AnyAsync(access =>
                    !access.AccessFeatures.Any() ||
                    access.AccessFeatures.Any(feature =>
                        feature.IsAllow &&
                        feature.Feature != null &&
                        feature.Feature.FeatureKey == "MENU_" + access.MenuId + "_" + normalizedAction),
                    ct);
        }

        /// <summary>
        /// Resolves the StaffId (Guid as string) for the currently logged-in user.
        /// Returns null if the user has no Staff record (e.g. pure admin account).
        /// </summary>
        private async Task<string?> GetCurrentStaffIdAsync()
        {
            var staffIdClaim = User.FindFirstValue(AccountClaimTypes.StaffId);
            if (Guid.TryParse(staffIdClaim, out var claimedStaffId))
                return claimedStaffId.ToString();

            var identityUserId = await ResolveIdentityUserIdAsync();
            if (string.IsNullOrWhiteSpace(identityUserId)) return null;

            var staffId = await _db.Persons.AsNoTracking()
                .Where(person => person.IdentityUserId == identityUserId)
                .Select(person => person.Staff != null
                    ? (Guid?)person.Staff.StaffId
                    : null)
                .FirstOrDefaultAsync();
            return staffId?.ToString();
        }

        /// <summary>
        /// Returns the staffId to use for note filtering.
        /// Falls back to the identity user ID so admin-only accounts still work.
        /// </summary>
        private async Task<string> ResolveStaffIdAsync()
        {
            var staffId = await GetCurrentStaffIdAsync();
            var identityUserId = await ResolveIdentityUserIdAsync();
            return staffId ?? identityUserId ?? "anonymous";
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
            var identityUserId = await ResolveIdentityUserIdAsync();
            if (string.IsNullOrWhiteSpace(identityUserId))
                return Unauthorized(new { message = "Unable to resolve logged-in user identity." });

            var staffId = await ResolveStaffIdAsync();
            try
            {
                var data = await _service.GetVisibleAsync(staffId, identityUserId, menuCode, entityType, entityId, ct);
                return Ok(CommApiResponse<List<AppNoteDto>>.Ok(data));
            }
            catch (OperationCanceledException)
            {
                // Client canceled request (navigation/re-render). Return safe empty payload.
                return Ok(CommApiResponse<List<AppNoteDto>>.Ok(new List<AppNoteDto>()));
            }
        }

        /// <summary>
        /// Admin instructions shown after login (read-only for recipients).
        /// Frontend: show popups where isPopup === true; list all in instructions panel.
        /// </summary>
        [HttpGet("login-instructions")]
        public async Task<IActionResult> GetLoginInstructions(CancellationToken ct)
        {
            var identityUserId = await ResolveIdentityUserIdAsync();
            if (string.IsNullOrWhiteSpace(identityUserId))
                return Unauthorized(new { message = "Unable to resolve logged-in user identity." });

            var staffId = await ResolveStaffIdAsync();
            try
            {
                var data = await _service.GetLoginInstructionsAsync(staffId, identityUserId, ct);
                return Ok(CommApiResponse<List<AppNoteDto>>.Ok(data));
            }
            catch (OperationCanceledException)
            {
                return Ok(CommApiResponse<List<AppNoteDto>>.Ok(new List<AppNoteDto>()));
            }
        }

        /// <summary>All admin instructions — admin CRUD management only.</summary>
        [HttpGet("admin/instructions")]
        public async Task<IActionResult> GetAdminInstructions(CancellationToken ct)
        {
            if (!await HasInstructionPermissionAsync("VIEW", ct))
                return Forbid();

            var identityUserId = await ResolveIdentityUserIdAsync();
            if (string.IsNullOrWhiteSpace(identityUserId))
                return Unauthorized(new { message = "Unable to resolve logged-in user identity." });

            var data = await _service.GetAdminInstructionsAsync(identityUserId, ct);
            return Ok(CommApiResponse<List<AdminInstructionDto>>.Ok(data));
        }

        [HttpGet("admin/audience-scope")]
        public async Task<IActionResult> GetInstructionAudienceScope(CancellationToken ct)
        {
            if (!await HasInstructionPermissionAsync("VIEW", ct))
                return Forbid();

            var identityUserId = await ResolveIdentityUserIdAsync();
            if (string.IsNullOrWhiteSpace(identityUserId))
                return Unauthorized(new { message = "Unable to resolve logged-in user identity." });

            var data = await _service.GetInstructionAudienceScopeAsync(identityUserId, ct);
            return Ok(CommApiResponse<InstructionAudienceScopeDto>.Ok(data));
        }

        /// <summary>Get a single note by ID.</summary>
        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id, CancellationToken ct)
        {
            var identityUserId = await ResolveIdentityUserIdAsync();
            if (string.IsNullOrWhiteSpace(identityUserId))
                return Unauthorized(new { message = "Unable to resolve logged-in user identity." });

            var staffId = await ResolveStaffIdAsync();
            AppNoteDto data;
            try
            {
                data = await _service.GetByIdAsync(id, staffId, identityUserId, ct);
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
            catch (OperationCanceledException)
            {
                return Ok(CommApiResponse<AppNoteDto>.Fail("Request cancelled by client."));
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
            var identityUserId = await ResolveIdentityUserIdAsync();
            if (string.IsNullOrWhiteSpace(identityUserId))
                return Unauthorized(new { message = "Unable to resolve logged-in user identity." });

            var staffId = await ResolveStaffIdAsync();
            try
            {
                var count = await _service.GetUnreadCountAsync(staffId, identityUserId, menuCode, ct);
                return Ok(CommApiResponse<int>.Ok(count));
            }
            catch (OperationCanceledException)
            {
                return Ok(CommApiResponse<int>.Ok(0));
            }
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
            var identityUserId = await ResolveIdentityUserIdAsync();
            if (string.IsNullOrWhiteSpace(identityUserId))
                return Unauthorized(new { message = "Unable to resolve logged-in user identity." });

            var canCreateAdminInstruction = await HasInstructionPermissionAsync("ADD", ct);
            if (!canCreateAdminInstruction)
            {
                if (request.SourceTypeCode.Trim().Equals("ADMIN", StringComparison.OrdinalIgnoreCase))
                    return StatusCode(StatusCodes.Status403Forbidden, new { message = "You do not have permission to create instructions." });

                // Regular users can only create personal notes
                request.SourceTypeCode     = "USER";
                request.VisibilityTypeCode = "PRIVATE";
                request.Targets            = new List<AppNoteTargetRequest>();
            }
            else if (request.SourceTypeCode.Trim().Equals("ADMIN", StringComparison.OrdinalIgnoreCase))
            {
                // Admin broadcast instructions — never USER/PRIVATE
                request.SourceTypeCode = "ADMIN";
            }

            // Use CancellationToken.None for writes so notes still save even if client aborts request.
            try
            {
                var data = await _service.CreateAsync(request, identityUserId, CancellationToken.None);
                return Ok(CommApiResponse<AppNoteDto>.Ok(data, "Note created successfully."));
            }
            catch (UnauthorizedAccessException ex)
            {
                return Forbid(ex.Message);
            }
        }

        // ── Status actions ────────────────────────────────────────────────────

        /// <summary>Mark a note as read (per-staff).</summary>
        [HttpPost("{id:int}/mark-read")]
        public async Task<IActionResult> MarkRead(int id, CancellationToken ct)
        {
            var staffId = await ResolveStaffIdAsync();
            await _service.MarkReadAsync(id, staffId, CancellationToken.None);
            return Ok(CommApiResponse<object>.Ok(null!, "Marked as read."));
        }

        /// <summary>Acknowledge a note (per-staff).</summary>
        [HttpPost("{id:int}/acknowledge")]
        public async Task<IActionResult> Acknowledge(int id, CancellationToken ct)
        {
            var staffId = await ResolveStaffIdAsync();
            await _service.AcknowledgeAsync(id, staffId, CancellationToken.None);
            return Ok(CommApiResponse<object>.Ok(null!, "Acknowledged."));
        }

        /// <summary>Dismiss a note (per-staff, only if AllowDismiss = true).</summary>
        [HttpPost("{id:int}/dismiss")]
        public async Task<IActionResult> Dismiss(int id, CancellationToken ct)
        {
            var staffId = await ResolveStaffIdAsync();
            await _service.DismissAsync(id, staffId, CancellationToken.None);
            return Ok(CommApiResponse<object>.Ok(null!, "Dismissed."));
        }

        // ── Edit / Delete — creator or admin only ─────────────────────────────

        /// <summary>Update a note. Only the creator or an admin can edit.</summary>
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(
            int id, [FromBody] CreateAppNoteRequest request, CancellationToken ct)
        {
            var identityUserId = await ResolveIdentityUserIdAsync();
            if (string.IsNullOrWhiteSpace(identityUserId))
                return Unauthorized(new { message = "Unable to resolve logged-in user identity." });

            var staffId  = await ResolveStaffIdAsync();
            AppNoteDto existing;
            try
            {
                existing = await _service.GetByIdAsync(id, staffId, identityUserId, ct);
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }

            var canEditInstruction = await HasInstructionPermissionAsync("EDIT", ct);
            if (!canEditInstruction && existing.CreatedBy != identityUserId)
                return Forbid();

            // Admin instructions are read-only for recipients — only admin can edit
            if (!canEditInstruction && existing.SourceTypeCode == "ADMIN")
                return Forbid();

            try
            {
                var data = await _service.UpdateAsync(id, request, identityUserId, CancellationToken.None);
                return Ok(CommApiResponse<AppNoteDto>.Ok(data, "Note updated successfully."));
            }
            catch (UnauthorizedAccessException ex)
            {
                return Forbid(ex.Message);
            }
        }

        /// <summary>Delete a note. Only the creator or an admin can delete.</summary>
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id, CancellationToken ct)
        {
            var identityUserId = await ResolveIdentityUserIdAsync();
            if (string.IsNullOrWhiteSpace(identityUserId))
                return Unauthorized(new { message = "Unable to resolve logged-in user identity." });

            var staffId  = await ResolveStaffIdAsync();
            AppNoteDto existing;
            try
            {
                existing = await _service.GetByIdAsync(id, staffId, identityUserId, ct);
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }

            var canDeleteInstruction = await HasInstructionPermissionAsync("DELETE", ct);
            if (!canDeleteInstruction && existing.CreatedBy != identityUserId)
                return Forbid();

            if (!canDeleteInstruction && existing.SourceTypeCode == "ADMIN")
                return Forbid();

            await _service.DeleteAsync(id, identityUserId, CancellationToken.None);
            return Ok(CommApiResponse<object>.Ok(null!, "Note deleted."));
        }
    }
}
