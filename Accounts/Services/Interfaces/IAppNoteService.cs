using Accounts.DTOs.CommCenter;
using Accounts.Models;

namespace Accounts.Services.Interfaces
{
    public interface IAppNoteService
    {
        /// <summary>
        /// Returns notes visible to the given staff member.
        /// Filtering is done entirely on the backend — targets (ALL / STAFF / MENU / RECORD).
        /// </summary>
        Task<List<AppNoteDto>> GetVisibleAsync(
            string staffId,
            string identityUserId,
            string? menuCode,
            string? entityType,
            string? entityId,
            CancellationToken ct);

        Task<AppNoteDto> GetByIdAsync(int noteId, string staffId, string identityUserId, CancellationToken ct);

        Task<AppNoteDto> CreateAsync(CreateAppNoteRequest request, string createdByUserId, CancellationToken ct);

        Task<AppNoteDto> UpdateAsync(int noteId, CreateAppNoteRequest request, string updatedByUserId, CancellationToken ct);

        Task DeleteAsync(int noteId, string deletedByUserId, CancellationToken ct);

        Task MarkReadAsync(int noteId, string staffId, CancellationToken ct);

        Task AcknowledgeAsync(int noteId, string staffId, CancellationToken ct);

        Task DismissAsync(int noteId, string staffId, CancellationToken ct);

        /// <summary>Count of unread ADMIN notes visible to this staff member.</summary>
        Task<int> GetUnreadCountAsync(string staffId, string identityUserId, string? menuCode, CancellationToken ct);

        /// <summary>Admin instructions visible on login (read-only for recipients).</summary>
        Task<List<AppNoteDto>> GetLoginInstructionsAsync(
            string staffId, string identityUserId, CancellationToken ct);

        /// <summary>Instruction audience available to the current administrator/manager.</summary>
        Task<InstructionAudienceScopeDto> GetInstructionAudienceScopeAsync(string identityUserId, CancellationToken ct);

        /// <summary>Admin instructions visible in the management UI for this administrator/manager scope.</summary>
        Task<List<AdminInstructionDto>> GetAdminInstructionsAsync(string identityUserId, CancellationToken ct);
    }
}
