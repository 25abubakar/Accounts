using Accounts.Data;
using Accounts.DTOs.CommCenter;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Services
{
    public class AppNoteService : IAppNoteService
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<AppNoteService> _logger;

        public AppNoteService(ApplicationDbContext db, ILogger<AppNoteService> logger)
        {
            _db     = db;
            _logger = logger;
        }

        // ── Get Visible Notes ─────────────────────────────────────────────────
        public async Task<List<AppNoteDto>> GetVisibleAsync(
            string userId, IList<string> roles,
            string? menuCode, string? entityType, string? entityId,
            CancellationToken ct)
        {
            var now = DateTime.UtcNow;

            var notes = await _db.AppNotes
                .AsNoTracking()
                .Include(n => n.Targets)
                .Include(n => n.UserStatuses)
                .Where(n => n.IsActive && !n.IsDeleted && n.IsPublished
                         && (n.StartDateUtc == null || n.StartDateUtc <= now)
                         && (n.EndDateUtc   == null || n.EndDateUtc   >= now))
                .ToListAsync(ct);

            var visible = notes
                .Where(n => IsVisible(n, userId, roles, menuCode, entityType, entityId))
                .Where(n => !IsDismissed(n, userId))
                .OrderByDescending(n => n.IsPinned)
                .ThenBy(n => PriorityRank(n.PriorityCode))
                .ThenByDescending(n => n.CreatedOnUtc)
                .Select(n => ToDto(n, userId))
                .ToList();

            return visible;
        }

        // ── Get By Id ─────────────────────────────────────────────────────────
        public async Task<AppNoteDto> GetByIdAsync(int noteId, string userId, CancellationToken ct)
        {
            var note = await GetNoteOrThrowAsync(noteId, ct);
            return ToDto(note, userId);
        }

        // ── Create ────────────────────────────────────────────────────────────
        public async Task<AppNoteDto> CreateAsync(
            CreateAppNoteRequest request, string userId, CancellationToken ct)
        {
            Validate(request);

            var note = new AppNote
            {
                Title                 = request.Title.Trim(),
                NoteBody              = request.NoteBody.Trim(),
                NoteTypeCode          = request.NoteTypeCode.Trim(),
                SourceTypeCode        = request.SourceTypeCode.Trim(),
                CategoryCode          = Norm(request.CategoryCode),
                PriorityCode          = request.PriorityCode.Trim(),
                VisibilityTypeCode    = request.VisibilityTypeCode.Trim(),
                MenuCode              = Norm(request.MenuCode),
                ModuleName            = Norm(request.ModuleName),
                EntityType            = Norm(request.EntityType),
                EntityId              = Norm(request.EntityId),
                StartDateUtc          = request.StartDateUtc,
                EndDateUtc            = request.EndDateUtc,
                IsPublished           = request.IsPublished,
                IsPinned              = request.IsPinned,
                IsPopup               = request.IsPopup,
                RequireAcknowledgement = request.RequireAcknowledgement,
                AllowDismiss          = request.AllowDismiss,
                CreatedBy             = userId,
                CreatedOnUtc          = DateTime.UtcNow
            };

            foreach (var t in request.Targets)
                note.Targets.Add(new AppNoteTarget
                {
                    TargetTypeCode = t.TargetTypeCode.Trim(),
                    TargetValue    = t.TargetValue.Trim(),
                    CreatedOnUtc   = DateTime.UtcNow
                });

            _db.AppNotes.Add(note);
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("AppNote created. NoteId={NoteId} By={UserId}", note.NoteId, userId);
            return ToDto(note, userId);
        }

        // ── Update ────────────────────────────────────────────────────────────
        public async Task<AppNoteDto> UpdateAsync(
            int noteId, CreateAppNoteRequest request, string userId, CancellationToken ct)
        {
            Validate(request);
            var note = await GetNoteOrThrowAsync(noteId, ct);

            note.Title                 = request.Title.Trim();
            note.NoteBody              = request.NoteBody.Trim();
            note.NoteTypeCode          = request.NoteTypeCode.Trim();
            note.SourceTypeCode        = request.SourceTypeCode.Trim();
            note.CategoryCode          = Norm(request.CategoryCode);
            note.PriorityCode          = request.PriorityCode.Trim();
            note.VisibilityTypeCode    = request.VisibilityTypeCode.Trim();
            note.MenuCode              = Norm(request.MenuCode);
            note.ModuleName            = Norm(request.ModuleName);
            note.EntityType            = Norm(request.EntityType);
            note.EntityId              = Norm(request.EntityId);
            note.StartDateUtc          = request.StartDateUtc;
            note.EndDateUtc            = request.EndDateUtc;
            note.IsPublished           = request.IsPublished;
            note.IsPinned              = request.IsPinned;
            note.IsPopup               = request.IsPopup;
            note.RequireAcknowledgement = request.RequireAcknowledgement;
            note.AllowDismiss          = request.AllowDismiss;
            note.UpdatedBy             = userId;
            note.UpdatedOnUtc          = DateTime.UtcNow;

            // Replace targets
            var oldTargets = await _db.AppNoteTargets.Where(t => t.NoteId == noteId).ToListAsync(ct);
            _db.AppNoteTargets.RemoveRange(oldTargets);

            foreach (var t in request.Targets)
                note.Targets.Add(new AppNoteTarget
                {
                    TargetTypeCode = t.TargetTypeCode.Trim(),
                    TargetValue    = t.TargetValue.Trim(),
                    CreatedOnUtc   = DateTime.UtcNow
                });

            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("AppNote updated. NoteId={NoteId} By={UserId}", noteId, userId);
            return ToDto(note, userId);
        }

        // ── Delete (soft) ─────────────────────────────────────────────────────
        public async Task DeleteAsync(int noteId, string userId, CancellationToken ct)
        {
            var note = await GetNoteOrThrowAsync(noteId, ct);
            note.IsDeleted    = true;
            note.DeletedBy    = userId;
            note.DeletedOnUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("AppNote deleted. NoteId={NoteId} By={UserId}", noteId, userId);
        }

        // ── Mark Read ─────────────────────────────────────────────────────────
        public async Task MarkReadAsync(int noteId, string userId, CancellationToken ct)
        {
            var status = await GetOrCreateStatusAsync(noteId, userId, ct);
            status.IsRead    = true;
            status.ReadOnUtc ??= DateTime.UtcNow;
            status.UpdatedOnUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        // ── Acknowledge ───────────────────────────────────────────────────────
        public async Task AcknowledgeAsync(int noteId, string userId, CancellationToken ct)
        {
            var status = await GetOrCreateStatusAsync(noteId, userId, ct);
            status.IsRead              = true;
            status.ReadOnUtc           ??= DateTime.UtcNow;
            status.IsAcknowledged      = true;
            status.AcknowledgedOnUtc   ??= DateTime.UtcNow;
            status.UpdatedOnUtc        = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        // ── Dismiss ───────────────────────────────────────────────────────────
        public async Task DismissAsync(int noteId, string userId, CancellationToken ct)
        {
            var note = await GetNoteOrThrowAsync(noteId, ct);
            if (!note.AllowDismiss)
                throw new InvalidOperationException("This note cannot be dismissed.");

            var status = await GetOrCreateStatusAsync(noteId, userId, ct);
            status.IsDismissed    = true;
            status.DismissedOnUtc ??= DateTime.UtcNow;
            status.UpdatedOnUtc   = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        // ── Unread Count ──────────────────────────────────────────────────────
        public async Task<int> GetUnreadCountAsync(
            string userId, IList<string> roles, string? menuCode, CancellationToken ct)
        {
            var visible = await GetVisibleAsync(userId, roles, menuCode, null, null, ct);
            return visible.Count(n => n.SourceTypeCode == "ADMIN" && !n.IsRead);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private async Task<AppNote> GetNoteOrThrowAsync(int noteId, CancellationToken ct)
        {
            var note = await _db.AppNotes
                .Include(n => n.Targets)
                .Include(n => n.UserStatuses)
                .Include(n => n.Attachments)
                .FirstOrDefaultAsync(n => n.NoteId == noteId && !n.IsDeleted, ct);

            return note ?? throw new KeyNotFoundException($"Note {noteId} not found.");
        }

        private async Task<AppNoteUserStatus> GetOrCreateStatusAsync(
            int noteId, string userId, CancellationToken ct)
        {
            var status = await _db.AppNoteUserStatuses
                .FirstOrDefaultAsync(s => s.NoteId == noteId && s.UserId == userId, ct);

            if (status != null) return status;

            status = new AppNoteUserStatus
            {
                NoteId       = noteId,
                UserId       = userId,
                CreatedOnUtc = DateTime.UtcNow
            };
            _db.AppNoteUserStatuses.Add(status);
            return status;
        }

        private static bool IsVisible(
            AppNote note, string userId, IList<string> roles,
            string? menuCode, string? entityType, string? entityId)
        {
            // User notes: only visible to creator
            if (note.SourceTypeCode == "USER" && note.CreatedBy != userId)
                return false;

            return note.VisibilityTypeCode switch
            {
                "GENERAL"  => true,
                "ALL_USERS" => string.IsNullOrEmpty(note.MenuCode) && string.IsNullOrEmpty(note.EntityType),
                "MENU"     => string.Equals(note.MenuCode, menuCode, StringComparison.OrdinalIgnoreCase),
                "RECORD"   => SameRecord(note, entityType, entityId),
                "PRIVATE"  => note.CreatedBy == userId,
                "USER"     => note.Targets.Any(t => t.IsActive && t.TargetTypeCode == "USER" && t.TargetValue == userId),
                "ROLE"     => note.Targets.Any(t => t.IsActive && t.TargetTypeCode == "ROLE" && roles.Contains(t.TargetValue)),
                _          => true
            };
        }

        private static bool SameRecord(AppNote note, string? entityType, string? entityId) =>
            !string.IsNullOrWhiteSpace(entityType) &&
            !string.IsNullOrWhiteSpace(entityId) &&
            string.Equals(note.EntityType, entityType, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(note.EntityId,   entityId,   StringComparison.OrdinalIgnoreCase);

        private static bool IsDismissed(AppNote note, string userId) =>
            note.UserStatuses.Any(s => s.UserId == userId && s.IsDismissed);

        private static AppNoteDto ToDto(AppNote note, string userId)
        {
            var status = note.UserStatuses.FirstOrDefault(s => s.UserId == userId);
            return new AppNoteDto
            {
                NoteId                = note.NoteId,
                Title                 = note.Title,
                NoteBody              = note.NoteBody,
                NoteTypeCode          = note.NoteTypeCode,
                SourceTypeCode        = note.SourceTypeCode,
                CategoryCode          = note.CategoryCode,
                PriorityCode          = note.PriorityCode,
                VisibilityTypeCode    = note.VisibilityTypeCode,
                MenuCode              = note.MenuCode,
                ModuleName            = note.ModuleName,
                EntityType            = note.EntityType,
                EntityId              = note.EntityId,
                IsPublished           = note.IsPublished,
                IsPinned              = note.IsPinned,
                IsPopup               = note.IsPopup,
                RequireAcknowledgement = note.RequireAcknowledgement,
                AllowDismiss          = note.AllowDismiss,
                IsRead                = status?.IsRead ?? note.SourceTypeCode == "USER",
                IsAcknowledged        = status?.IsAcknowledged ?? false,
                IsDismissed           = status?.IsDismissed ?? false,
                CreatedBy             = note.CreatedBy,
                CreatedOnUtc          = note.CreatedOnUtc
            };
        }

        private static int PriorityRank(string code) => code switch
        {
            "CRITICAL" => 1, "HIGH" => 2, "NORMAL" => 3, "LOW" => 4, _ => 9
        };

        private static void Validate(CreateAppNoteRequest r)
        {
            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(r.Title))          errors.Add("Title is required.");
            if (string.IsNullOrWhiteSpace(r.NoteBody))       errors.Add("Note body is required.");
            if (string.IsNullOrWhiteSpace(r.NoteTypeCode))   errors.Add("Note type is required.");
            if (string.IsNullOrWhiteSpace(r.SourceTypeCode)) errors.Add("Source type is required.");
            if (string.IsNullOrWhiteSpace(r.PriorityCode))   errors.Add("Priority is required.");
            if (string.IsNullOrWhiteSpace(r.VisibilityTypeCode)) errors.Add("Visibility type is required.");
            if (r.StartDateUtc.HasValue && r.EndDateUtc.HasValue && r.EndDateUtc < r.StartDateUtc)
                errors.Add("End date cannot be before start date.");
            if (errors.Any())
                throw new ArgumentException(string.Join(" | ", errors));
        }

        private static string? Norm(string? v) =>
            string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    }
}
