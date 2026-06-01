using Accounts.DTOs.CommCenter;

namespace Accounts.Services.Interfaces
{
    public interface IAppNoteService
    {
        Task<List<AppNoteDto>> GetVisibleAsync(string userId, IList<string> roles,
            string? menuCode, string? entityType, string? entityId, CancellationToken ct);

        Task<AppNoteDto> GetByIdAsync(int noteId, string userId, CancellationToken ct);

        Task<AppNoteDto> CreateAsync(CreateAppNoteRequest request, string userId, CancellationToken ct);

        Task<AppNoteDto> UpdateAsync(int noteId, CreateAppNoteRequest request, string userId, CancellationToken ct);

        Task DeleteAsync(int noteId, string userId, CancellationToken ct);

        Task MarkReadAsync(int noteId, string userId, CancellationToken ct);

        Task AcknowledgeAsync(int noteId, string userId, CancellationToken ct);

        Task DismissAsync(int noteId, string userId, CancellationToken ct);

        Task<int> GetUnreadCountAsync(string userId, IList<string> roles, string? menuCode, CancellationToken ct);
    }
}
