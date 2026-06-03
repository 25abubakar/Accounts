using Accounts.Data;
using Accounts.DTOs.CommCenter;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Services
{
    /// <summary>
    /// Communication Center — targeted notes / instructions.
    ///
    /// Visibility is resolved entirely on the backend using AppNoteTargets:
    ///   ALL    / *                  → visible to every authenticated staff member
    ///   STAFF  / {staffId}          → visible only to that specific staff member
    ///   MENU   / {menuCode}         → visible only on that menu page
    ///   RECORD / {entityType}:{id}  → visible only on that record page
    ///
    /// Per-staff read / acknowledge / dismiss state is stored in AppNoteUserStates.
    /// </summary>
    public class AppNoteService : IAppNoteService
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<AppNoteService> _logger;

        public AppNoteService(ApplicationDbContext db, ILogger<AppNoteService> logger)
        {
            _db     = db;
            _logger = logger;
        }

        // ── Get Visible ───────────────────────────────────────────────────────

        public async Task<List<AppNoteDto>> GetVisibleAsync(
            string staffId,
            string identityUserId,
            string? menuCode,
            string? entityType,
            string? entityId,
            CancellationToken ct)
        {
            var now = DateTime.UtcNow;
            var recordKey = (entityType != null && entityId != null)
                ? $"{entityType}:{entityId}"
                : null;

            // Step 1: fetch all published, active, non-deleted notes within date range
            var candidates = await _db.AppNotes
                .AsNoTracking()
                .Include(n => n.Targets)
                .Where(n => n.IsPublished && n.IsActive && !n.IsDeleted)
                .Where(n => n.StartDateUtc == null || n.StartDateUtc <= now)
                .Where(n => n.EndDateUtc   == null || n.EndDateUtc   >= now)
                .ToListAsync(CancellationToken.None);

            // Step 2: apply privacy rules in memory
            var notes = candidates.Where(n =>
            {
                // ── USER notes — strictly private to creator ──────────────────
                // Only the person who created it can see it.
                if (n.SourceTypeCode == "USER")
                    return n.OwnerIdentityUserId == identityUserId || n.CreatedBy == identityUserId;

                // ── ADMIN notes — filter by VisibilityTypeCode + Targets ──────
                if (n.SourceTypeCode == "ADMIN")
                {
                    return (n.VisibilityTypeCode?.ToUpper()) switch
                    {
                        // GENERAL / ALL_USERS → everyone sees it
                        "GENERAL"   => true,
                        "ALL_USERS" => true,

                        // STAFF → only if staffId is in AppNoteTargets
                        "STAFF"  => n.Targets.Any(t =>
                            t.TargetTypeCode == "STAFF" && t.TargetValue == staffId),

                        // MENU → only on that specific menu page
                        "MENU" => menuCode != null &&
                                  n.Targets.Any(t =>
                                      t.TargetTypeCode == "MENU" && t.TargetValue == menuCode),

                        // RECORD → only on that specific record page
                        "RECORD" => recordKey != null &&
                                    n.Targets.Any(t =>
                                        t.TargetTypeCode == "RECORD" && t.TargetValue == recordKey),

                        // Unknown visibility → deny
                        _ => false
                    };
                }

                // Any other source type — use target-based matching (ALL / STAFF / MENU / RECORD)
                return n.Targets.Any(t => t.TargetTypeCode == "ALL"   && t.TargetValue == "*") ||
                       n.Targets.Any(t => t.TargetTypeCode == "STAFF" && t.TargetValue == staffId) ||
                       (menuCode  != null && n.Targets.Any(t => t.TargetTypeCode == "MENU"   && t.TargetValue == menuCode)) ||
                       (recordKey != null && n.Targets.Any(t => t.TargetTypeCode == "RECORD" && t.TargetValue == recordKey));
            }).ToList();

            // Sort in memory
            notes = notes
                .OrderByDescending(n => n.IsPinned)
                .ThenBy(n => PriorityRank(n.PriorityCode))
                .ThenByDescending(n => n.CreatedOnUtc)
                .ToList();

            if (!notes.Any())
                return new List<AppNoteDto>();

            var noteIds = notes.Select(n => n.NoteId).ToList();

            // Step 3: load per-staff states
            var states = await _db.AppNoteUserStates
                .AsNoTracking()
                .Where(s => noteIds.Contains(s.NoteId) && s.StaffId == staffId)
                .ToListAsync(CancellationToken.None);

            var stateMap = states.ToDictionary(s => s.NoteId);

            // Step 4: exclude dismissed, map to DTOs
            return notes
                .Where(n => !stateMap.TryGetValue(n.NoteId, out var st) || !st.IsDismissed)
                .Select(n =>
                {
                    stateMap.TryGetValue(n.NoteId, out var state);
                    return ToDto(n, state);
                })
                .ToList();
        }

        // ── Get By Id ─────────────────────────────────────────────────────────

        public async Task<AppNoteDto> GetByIdAsync(int noteId, string staffId, string identityUserId, CancellationToken ct)
        {
            var note = await _db.AppNotes
                .AsNoTracking()
                .Include(n => n.Targets)
                .FirstOrDefaultAsync(n => n.NoteId == noteId && !n.IsDeleted, CancellationToken.None)
                ?? throw new KeyNotFoundException($"Note {noteId} not found.");

            if (note.SourceTypeCode == "USER" &&
                note.OwnerIdentityUserId != identityUserId &&
                note.CreatedBy != identityUserId)
            {
                throw new UnauthorizedAccessException("You are not allowed to view this note.");
            }

            var state = await _db.AppNoteUserStates
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.NoteId == noteId && s.StaffId == staffId, CancellationToken.None);

            return ToDto(note, state);
        }

        // ── Create ────────────────────────────────────────────────────────────

        public async Task<AppNoteDto> CreateAsync(
            CreateAppNoteRequest request, string createdByUserId, CancellationToken ct)
        {
            Validate(request);

            var note = new AppNote
            {
                Title                  = request.Title.Trim(),
                NoteBody               = request.NoteBody.Trim(),
                NoteTypeCode           = request.NoteTypeCode.Trim(),
                SourceTypeCode         = request.SourceTypeCode.Trim(),
                CategoryCode           = Norm(request.CategoryCode),
                PriorityCode           = request.PriorityCode.Trim(),
                VisibilityTypeCode     = request.VisibilityTypeCode.Trim(),
                MenuCode               = Norm(request.MenuCode),
                ModuleName             = Norm(request.ModuleName),
                EntityType             = Norm(request.EntityType),
                EntityId               = Norm(request.EntityId),
                StartDateUtc           = request.StartDateUtc,
                EndDateUtc             = request.EndDateUtc,
                IsPublished            = request.IsPublished,
                IsPinned               = request.IsPinned,
                IsPopup                = request.IsPopup,
                RequireAcknowledgement = request.RequireAcknowledgement,
                AllowDismiss           = request.AllowDismiss,
                CreatedBy              = createdByUserId,
                OwnerIdentityUserId    = request.SourceTypeCode.Trim() == "USER" ? createdByUserId : null,
                CreatedOnUtc           = DateTime.UtcNow
            };

            // Build targets
            if (note.SourceTypeCode == "USER")
            {
                // Personal notes are owner-scoped; no broadcast target rows.
            }
            else if (request.Targets != null && request.Targets.Count > 0)
            {
                foreach (var t in request.Targets)
                    note.Targets.Add(new AppNoteTarget
                    {
                        TargetTypeCode = t.TargetTypeCode.Trim(),
                        TargetValue    = t.TargetValue.Trim()
                    });
            }
            else
            {
                // No targets supplied → default to ALL users
                note.Targets.Add(new AppNoteTarget
                {
                    TargetTypeCode = "ALL",
                    TargetValue    = "*"
                });
            }

            _db.AppNotes.Add(note);
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("AppNote created. NoteId={NoteId} By={UserId}", note.NoteId, createdByUserId);
            return ToDto(note, (AppNoteUserState?)null);
        }

        // ── Update ────────────────────────────────────────────────────────────

        public async Task<AppNoteDto> UpdateAsync(
            int noteId, CreateAppNoteRequest request, string updatedByUserId, CancellationToken ct)
        {
            Validate(request);
            var note = await LoadNoteAsync(noteId, ct);

            note.Title                  = request.Title.Trim();
            note.NoteBody               = request.NoteBody.Trim();
            note.NoteTypeCode           = request.NoteTypeCode.Trim();
            note.SourceTypeCode         = request.SourceTypeCode.Trim();
            note.CategoryCode           = Norm(request.CategoryCode);
            note.PriorityCode           = request.PriorityCode.Trim();
            note.VisibilityTypeCode     = request.VisibilityTypeCode.Trim();
            note.MenuCode               = Norm(request.MenuCode);
            note.ModuleName             = Norm(request.ModuleName);
            note.EntityType             = Norm(request.EntityType);
            note.EntityId               = Norm(request.EntityId);
            note.StartDateUtc           = request.StartDateUtc;
            note.EndDateUtc             = request.EndDateUtc;
            note.IsPublished            = request.IsPublished;
            note.IsPinned               = request.IsPinned;
            note.IsPopup                = request.IsPopup;
            note.RequireAcknowledgement = request.RequireAcknowledgement;
            note.AllowDismiss           = request.AllowDismiss;
            if (note.SourceTypeCode == "USER" && string.IsNullOrWhiteSpace(note.OwnerIdentityUserId))
                note.OwnerIdentityUserId = updatedByUserId;
            note.UpdatedBy              = updatedByUserId;
            note.UpdatedOnUtc           = DateTime.UtcNow;

            // Replace targets
            var oldTargets = await _db.AppNoteTargets.Where(t => t.NoteId == noteId).ToListAsync(ct);
            _db.AppNoteTargets.RemoveRange(oldTargets);

            if (note.SourceTypeCode == "USER")
            {
                // Personal notes remain owner-scoped only.
            }
            else if (request.Targets != null && request.Targets.Count > 0)
            {
                foreach (var t in request.Targets)
                    note.Targets.Add(new AppNoteTarget
                    {
                        TargetTypeCode = t.TargetTypeCode.Trim(),
                        TargetValue    = t.TargetValue.Trim()
                    });
            }
            else
            {
                note.Targets.Add(new AppNoteTarget { TargetTypeCode = "ALL", TargetValue = "*" });
            }

            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("AppNote updated. NoteId={NoteId} By={UserId}", noteId, updatedByUserId);
            return ToDto(note, (AppNoteUserState?)null);
        }

        // ── Delete (soft) ─────────────────────────────────────────────────────

        public async Task DeleteAsync(int noteId, string deletedByUserId, CancellationToken ct)
        {
            var note = await LoadNoteAsync(noteId, ct);
            note.IsDeleted    = true;
            note.DeletedBy    = deletedByUserId;
            note.DeletedOnUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("AppNote deleted. NoteId={NoteId} By={UserId}", noteId, deletedByUserId);
        }

        // ── Mark Read ─────────────────────────────────────────────────────────

        public async Task MarkReadAsync(int noteId, string staffId, CancellationToken ct)
        {
            var state = await GetOrCreateStateAsync(noteId, staffId, ct);
            state.IsRead    = true;
            state.ReadOnUtc ??= DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        // ── Acknowledge ───────────────────────────────────────────────────────

        public async Task AcknowledgeAsync(int noteId, string staffId, CancellationToken ct)
        {
            var state = await GetOrCreateStateAsync(noteId, staffId, ct);
            state.IsRead             = true;
            state.ReadOnUtc          ??= DateTime.UtcNow;
            state.IsAcknowledged     = true;
            state.AcknowledgedOnUtc  ??= DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        // ── Dismiss ───────────────────────────────────────────────────────────

        public async Task DismissAsync(int noteId, string staffId, CancellationToken ct)
        {
            var note = await LoadNoteAsync(noteId, ct);
            if (!note.AllowDismiss)
                throw new InvalidOperationException("This note cannot be dismissed.");

            var state = await GetOrCreateStateAsync(noteId, staffId, ct);
            state.IsDismissed    = true;
            state.DismissedOnUtc ??= DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        // ── Unread Count ──────────────────────────────────────────────────────

        public async Task<int> GetUnreadCountAsync(
            string staffId, string identityUserId, string? menuCode, CancellationToken ct)
        {
            var visible = await GetVisibleAsync(staffId, identityUserId, menuCode, null, null, ct);
            return visible.Count(n => n.SourceTypeCode == "ADMIN" && !n.IsRead);
        }

        // ── Private helpers ───────────────────────────────────────────────────

        /// <summary>Load a note for write operations (includes Targets only).</summary>
        private async Task<AppNote> LoadNoteAsync(int noteId, CancellationToken ct)
        {
            var note = await _db.AppNotes
                .Include(n => n.Targets)
                .FirstOrDefaultAsync(n => n.NoteId == noteId && !n.IsDeleted, ct);

            return note ?? throw new KeyNotFoundException($"Note {noteId} not found.");
        }

        private async Task<AppNoteUserState> GetOrCreateStateAsync(
            int noteId, string staffId, CancellationToken ct)
        {
            var state = await _db.AppNoteUserStates
                .FirstOrDefaultAsync(s => s.NoteId == noteId && s.StaffId == staffId, ct);

            if (state != null) return state;

            state = new AppNoteUserState { NoteId = noteId, StaffId = staffId };
            _db.AppNoteUserStates.Add(state);
            return state;
        }

        private static AppNoteDto ToDto(AppNote note, AppNoteUserState? state)
        {
            return new AppNoteDto
            {
                NoteId                 = note.NoteId,
                Title                  = note.Title,
                NoteBody               = note.NoteBody,
                NoteTypeCode           = note.NoteTypeCode,
                SourceTypeCode         = note.SourceTypeCode,
                CategoryCode           = note.CategoryCode,
                PriorityCode           = note.PriorityCode,
                VisibilityTypeCode     = note.VisibilityTypeCode,
                MenuCode               = note.MenuCode,
                ModuleName             = note.ModuleName,
                EntityType             = note.EntityType,
                EntityId               = note.EntityId,
                IsPublished            = note.IsPublished,
                IsPinned               = note.IsPinned,
                IsPopup                = note.IsPopup,
                RequireAcknowledgement = note.RequireAcknowledgement,
                AllowDismiss           = note.AllowDismiss,
                IsRead                 = state?.IsRead ?? false,
                IsAcknowledged         = state?.IsAcknowledged ?? false,
                IsDismissed            = state?.IsDismissed ?? false,
                CreatedBy              = note.CreatedBy,
                CreatedOnUtc           = note.CreatedOnUtc
            };
        }

        private static int PriorityRank(string code) => code switch
        {
            "CRITICAL" => 1, "HIGH" => 2, "NORMAL" => 3, "LOW" => 4, _ => 9
        };

        private static void Validate(CreateAppNoteRequest r)
        {
            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(r.Title))              errors.Add("Title is required.");
            if (string.IsNullOrWhiteSpace(r.NoteBody))           errors.Add("Note body is required.");
            if (string.IsNullOrWhiteSpace(r.NoteTypeCode))       errors.Add("Note type is required.");
            if (string.IsNullOrWhiteSpace(r.SourceTypeCode))     errors.Add("Source type is required.");
            if (string.IsNullOrWhiteSpace(r.PriorityCode))       errors.Add("Priority is required.");
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
