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

        private async Task<string?> ResolveIdentityUserIdAsync()
        {
            var idFromClaims = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                               ?? User.FindFirst("sub")?.Value;

            if (!string.IsNullOrWhiteSpace(idFromClaims))
                return idFromClaims;

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

        private async Task<string> ResolveStaffIdAsync()
        {
            var staffId = await GetCurrentStaffIdAsync();
            var identityUserId = await ResolveIdentityUserIdAsync();
            return staffId ?? identityUserId ?? "anonymous";
        }


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
                return Ok(CommApiResponse<List<AppNoteDto>>.Ok(new List<AppNoteDto>()));
            }
        }

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

                request.SourceTypeCode     = "USER";
                request.VisibilityTypeCode = "PRIVATE";
                request.Targets            = new List<AppNoteTargetRequest>();
            }
            else if (request.SourceTypeCode.Trim().Equals("ADMIN", StringComparison.OrdinalIgnoreCase))
            {
                request.SourceTypeCode = "ADMIN";
            }

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

        [HttpPost("{id:int}/mark-read")]
        public async Task<IActionResult> MarkRead(int id, CancellationToken ct)
        {
            var staffId = await ResolveStaffIdAsync();
            await _service.MarkReadAsync(id, staffId, CancellationToken.None);
            return Ok(CommApiResponse<object>.Ok(null!, "Marked as read."));
        }

        [HttpPost("{id:int}/acknowledge")]
        public async Task<IActionResult> Acknowledge(int id, CancellationToken ct)
        {
            var staffId = await ResolveStaffIdAsync();
            await _service.AcknowledgeAsync(id, staffId, CancellationToken.None);
            return Ok(CommApiResponse<object>.Ok(null!, "Acknowledged."));
        }

        [HttpPost("{id:int}/dismiss")]
        public async Task<IActionResult> Dismiss(int id, CancellationToken ct)
        {
            var staffId = await ResolveStaffIdAsync();
            await _service.DismissAsync(id, staffId, CancellationToken.None);
            return Ok(CommApiResponse<object>.Ok(null!, "Dismissed."));
        }

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

            if (existing.SourceTypeCode == "ADMIN" &&
                !string.Equals(existing.CreatedBy, identityUserId, StringComparison.OrdinalIgnoreCase))
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

            if (existing.SourceTypeCode == "ADMIN" &&
                !string.Equals(existing.CreatedBy, identityUserId, StringComparison.OrdinalIgnoreCase))
                return Forbid();

            await _service.DeleteAsync(id, identityUserId, CancellationToken.None);
            return Ok(CommApiResponse<object>.Ok(null!, "Note deleted."));
        }
    }
}

