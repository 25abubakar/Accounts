using Accounts.DTOs.CommCenter;
using Accounts.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Accounts.Controllers
{
    [ApiController]
    [Route("api/app-notes")]
    public class AppNotesController : ControllerBase
    {
        private readonly IAppNoteService _service;

        public AppNotesController(IAppNoteService service) => _service = service;

        private string CurrentUserId =>
            User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value
            ?? "anonymous";

        private IList<string> CurrentRoles =>
            User.Claims.Where(c => c.Type == ClaimTypes.Role || c.Type == "role")
                       .Select(c => c.Value).ToList();

        // GET /api/app-notes/visible?menuCode=DASHBOARD&entityType=Patient&entityId=101
        [HttpGet("visible")]
        public async Task<IActionResult> GetVisible(
            [FromQuery] string? menuCode,
            [FromQuery] string? entityType,
            [FromQuery] string? entityId,
            CancellationToken ct)
        {
            var data = await _service.GetVisibleAsync(
                CurrentUserId, CurrentRoles, menuCode, entityType, entityId, ct);
            return Ok(CommApiResponse<List<AppNoteDto>>.Ok(data));
        }

        // GET /api/app-notes/{id}
        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id, CancellationToken ct)
        {
            var data = await _service.GetByIdAsync(id, CurrentUserId, ct);
            return Ok(CommApiResponse<AppNoteDto>.Ok(data));
        }

        // POST /api/app-notes
        [HttpPost]
        public async Task<IActionResult> Create(
            [FromBody] CreateAppNoteRequest request, CancellationToken ct)
        {
            var data = await _service.CreateAsync(request, CurrentUserId, ct);
            return Ok(CommApiResponse<AppNoteDto>.Ok(data, "Note created successfully."));
        }

        // PUT /api/app-notes/{id}
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(
            int id, [FromBody] CreateAppNoteRequest request, CancellationToken ct)
        {
            var data = await _service.UpdateAsync(id, request, CurrentUserId, ct);
            return Ok(CommApiResponse<AppNoteDto>.Ok(data, "Note updated successfully."));
        }

        // DELETE /api/app-notes/{id}
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id, CancellationToken ct)
        {
            await _service.DeleteAsync(id, CurrentUserId, ct);
            return Ok(CommApiResponse<object>.Ok(null!, "Note deleted."));
        }

        // POST /api/app-notes/{id}/mark-read
        [HttpPost("{id:int}/mark-read")]
        public async Task<IActionResult> MarkRead(int id, CancellationToken ct)
        {
            await _service.MarkReadAsync(id, CurrentUserId, ct);
            return Ok(CommApiResponse<object>.Ok(null!, "Marked as read."));
        }

        // POST /api/app-notes/{id}/acknowledge
        [HttpPost("{id:int}/acknowledge")]
        public async Task<IActionResult> Acknowledge(int id, CancellationToken ct)
        {
            await _service.AcknowledgeAsync(id, CurrentUserId, ct);
            return Ok(CommApiResponse<object>.Ok(null!, "Acknowledged."));
        }

        // POST /api/app-notes/{id}/dismiss
        [HttpPost("{id:int}/dismiss")]
        public async Task<IActionResult> Dismiss(int id, CancellationToken ct)
        {
            await _service.DismissAsync(id, CurrentUserId, ct);
            return Ok(CommApiResponse<object>.Ok(null!, "Dismissed."));
        }

        // GET /api/app-notes/unread-count?menuCode=DASHBOARD
        [HttpGet("unread-count")]
        public async Task<IActionResult> UnreadCount(
            [FromQuery] string? menuCode, CancellationToken ct)
        {
            var count = await _service.GetUnreadCountAsync(CurrentUserId, CurrentRoles, menuCode, ct);
            return Ok(CommApiResponse<int>.Ok(count));
        }
    }
}
