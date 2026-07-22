using Accounts.Data;
using Accounts.DTOs.CommCenter;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Services
{
    /// <summary>
    /// Communication Center — targeted notes / instructions.
    /// </summary>
    public class AppNoteService : IAppNoteService
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<AppNoteService> _logger;
        private readonly ITenantService _tenantService;

        public AppNoteService(
            ApplicationDbContext db,
            ILogger<AppNoteService> logger,
            ITenantService tenantService)
        {
            _db = db;
            _logger = logger;
            _tenantService = tenantService;
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

            // 🌟 SMART IDENTIFIERS LIST (Saari lowercase IDs isme jama hongi)
            var userIdentifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddIdentifier(userIdentifiers, staffId);
            AddIdentifier(userIdentifiers, identityUserId);

            string userName = string.Empty;
            string email = string.Empty;

            // ── STEP 1: Read UserName + Email from AspNetUsers ──
            // ApplicationUser extends IdentityUser — same table, same columns for UserName/Email.
            try
            {
                var connection = _db.Database.GetDbConnection();
                var wasClosed = connection.State == System.Data.ConnectionState.Closed;
                if (wasClosed) await connection.OpenAsync(ct);

                using (var command = connection.CreateCommand())
                {
                    command.CommandText =
                        "SELECT UserName, Email FROM dbo.AspNetUsers WHERE Id = @uid";
                    var parameter = command.CreateParameter();
                    parameter.ParameterName = "@uid";
                    parameter.Value = identityUserId.Trim();
                    command.Parameters.Add(parameter);

                    using (var reader = await command.ExecuteReaderAsync(ct))
                    {
                        if (await reader.ReadAsync(ct))
                        {
                            userName = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                            email    = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                        }
                    }
                }
                if (wasClosed) await connection.CloseAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning("AspNetUsers look up bypassed safely: {Message}", ex.Message);
            }

            // ── STEP 2: Resolve all user identifiers from StaffVacancy ──
            try
            {
                var connection = _db.Database.GetDbConnection();
                var wasClosed = connection.State == System.Data.ConnectionState.Closed;
                if (wasClosed) await connection.OpenAsync(ct);

                using (var command = connection.CreateCommand())
                {
                    // Match by LoginId = UserName (exact, case-insensitive)
                    command.CommandText = @"
                        SELECT
                            CAST(StaffId   AS VARCHAR(100)),
                            CAST(VacancyId AS VARCHAR(100)),
                            CAST(PersonId  AS VARCHAR(100))
                        FROM dbo.StaffVacancy
                        WHERE StaffId IS NOT NULL
                          AND LoginId IS NOT NULL
                          AND LoginId <> ''
                          AND (
                               LOWER(LoginId) = LOWER(@uname)
                            OR LOWER(LoginId) = LOWER(@email)
                          )";

                    var pUname = command.CreateParameter(); pUname.ParameterName = "@uname"; pUname.Value = userName.Length > 0 ? userName : "___NO_MATCH___"; command.Parameters.Add(pUname);
                    var pEmail = command.CreateParameter(); pEmail.ParameterName = "@email"; pEmail.Value = email.Length  > 0 ? email  : "___NO_MATCH___"; command.Parameters.Add(pEmail);

                    using (var reader = await command.ExecuteReaderAsync(ct))
                    {
                        while (await reader.ReadAsync(ct))
                        {
                            for (int i = 0; i < reader.FieldCount; i++)
                            {
                                AddIdentifier(userIdentifiers, reader.GetValue(i)?.ToString());
                            }
                        }
                    }
                }
                if (wasClosed) await connection.CloseAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning("StaffVacancy dynamic identifier lookup failed: {Message}", ex.Message);
            }

            // Step 1: fetch all published, active, non-deleted notes within date range
            var candidates = await _db.AppNotes
                .AsNoTracking()
                .Include(n => n.Targets)
                .Where(n => n.IsPublished && n.IsActive && !n.IsDeleted)
                .Where(n => n.StartDateUtc == null || n.StartDateUtc <= now)
                .Where(n => n.EndDateUtc == null || n.EndDateUtc >= now)
                .ToListAsync(ct);

            // Step 2: apply privacy rules in memory
            var notes = candidates.Where(n =>
            {
                // ── USER notes — strictly private to creator ──────────────────
                if (n.SourceTypeCode == "USER")
                    return n.OwnerIdentityUserId == identityUserId || n.CreatedBy == identityUserId;

                // ── ADMIN notes — filter by VisibilityTypeCode + Targets ──────
                if (n.SourceTypeCode == "ADMIN")
                {
                    var audienceTargets = n.Targets.Where(t =>
                        t.TargetTypeCode.Equals("ALL", StringComparison.OrdinalIgnoreCase) ||
                        t.TargetTypeCode.Equals("STAFF", StringComparison.OrdinalIgnoreCase)).ToList();
                    var menuTargets = n.Targets.Where(t =>
                        t.TargetTypeCode.Equals("MENU", StringComparison.OrdinalIgnoreCase)).ToList();
                    var recordTargets = n.Targets.Where(t =>
                        t.TargetTypeCode.Equals("RECORD", StringComparison.OrdinalIgnoreCase)).ToList();

                    if (audienceTargets.Count > 0 || menuTargets.Count > 0 || recordTargets.Count > 0)
                    {
                        var audienceMatches = audienceTargets.Count == 0 || audienceTargets.Any(t =>
                            t.TargetTypeCode.Equals("ALL", StringComparison.OrdinalIgnoreCase) ||
                            userIdentifiers.Contains(NormalizeIdentifier(t.TargetValue)));
                        var menuMatches = menuTargets.Count == 0 ||
                            (!string.IsNullOrWhiteSpace(menuCode) && menuTargets.Any(t =>
                                string.Equals(t.TargetValue, menuCode, StringComparison.OrdinalIgnoreCase)));
                        var recordMatches = recordTargets.Count == 0 ||
                            (!string.IsNullOrWhiteSpace(recordKey) && recordTargets.Any(t =>
                                string.Equals(t.TargetValue, recordKey, StringComparison.OrdinalIgnoreCase)));

                        return audienceMatches && menuMatches && recordMatches;
                    }

                    return (n.VisibilityTypeCode?.ToUpper()) switch
                    {
                        "GENERAL" => true,
                        "ALL_USERS" => true,

                        // STAFF → Matches if target value matches ANY resolved form of user identity
                        "STAFF" => n.Targets.Any(t =>
                            t.TargetTypeCode == "STAFF" && userIdentifiers.Contains(NormalizeIdentifier(t.TargetValue))),

                        "MENU" => menuCode != null &&
                                  n.Targets.Any(t =>
                                      t.TargetTypeCode == "MENU" && t.TargetValue == menuCode),

                        "RECORD" => recordKey != null &&
                                    n.Targets.Any(t =>
                                        t.TargetTypeCode == "RECORD" && t.TargetValue == recordKey),

                        _ => false
                    };
                }

                return n.Targets.Any(t => t.TargetTypeCode == "ALL" && t.TargetValue == "*") ||
                       n.Targets.Any(t => t.TargetTypeCode == "STAFF" && userIdentifiers.Contains(NormalizeIdentifier(t.TargetValue))) ||
                       (menuCode != null && n.Targets.Any(t => t.TargetTypeCode == "MENU" && t.TargetValue == menuCode)) ||
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
            var identifierList = userIdentifiers.ToList();

            // Step 3: load per-staff states
            var states = await _db.AppNoteUserStates
                .AsNoTracking()
                .Where(s => identifierList.Contains(s.StaffId) &&
                            noteIds.Contains(s.NoteId))
                .ToListAsync(ct);

            var stateMap = states
                .GroupBy(s => s.NoteId)
                .ToDictionary(g => g.Key, g => g.First());

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
                .FirstOrDefaultAsync(n => n.NoteId == noteId && !n.IsDeleted, ct)
                ?? throw new KeyNotFoundException($"Note {noteId} not found.");

            if (note.SourceTypeCode == "USER" &&
                note.OwnerIdentityUserId != identityUserId &&
                note.CreatedBy != identityUserId)
            {
                throw new UnauthorizedAccessException("You are not allowed to view this note.");
            }

            var userIdentifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddIdentifier(userIdentifiers, staffId);
            AddIdentifier(userIdentifiers, identityUserId);
            var identifierList = userIdentifiers.ToList();

            var state = await _db.AppNoteUserStates
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.NoteId == noteId && identifierList.Contains(s.StaffId), ct);

            return ToDto(note, state);
        }

        // ── Create ────────────────────────────────────────────────────────────

        public async Task<AppNoteDto> CreateAsync(
            CreateAppNoteRequest request, string createdByUserId, CancellationToken ct)
        {
            Validate(request);

            var note = new AppNote
            {
                TenantId = _tenantService.IsSuperAdmin ? null : _tenantService.TenantId,
                Title = request.Title.Trim(),
                NoteBody = request.NoteBody.Trim(),
                NoteTypeCode = request.NoteTypeCode.Trim(),
                SourceTypeCode = request.SourceTypeCode.Trim(),
                CategoryCode = Norm(request.CategoryCode),
                PriorityCode = request.PriorityCode.Trim(),
                VisibilityTypeCode = request.VisibilityTypeCode.Trim(),
                MenuCode = Norm(request.MenuCode),
                ModuleName = Norm(request.ModuleName),
                EntityType = Norm(request.EntityType),
                EntityId = Norm(request.EntityId),
                StartDateUtc = request.StartDateUtc,
                EndDateUtc = request.EndDateUtc,
                IsPublished = request.IsPublished,
                IsPinned = request.IsPinned,
                IsPopup = request.IsPopup,
                IsBanner = request.IsBanner,
                RequireAcknowledgement = request.RequireAcknowledgement,
                AllowDismiss = request.AllowDismiss,
                CreatedBy = createdByUserId,
                OwnerIdentityUserId = request.SourceTypeCode.Trim() == "USER" ? createdByUserId : null,
                CreatedOnUtc = DateTime.UtcNow
            };

            if (note.SourceTypeCode == "USER")
            {
            }
            else if (request.Targets != null && request.Targets.Count > 0)
            {
                foreach (var t in request.Targets)
                    note.Targets.Add(new AppNoteTarget
                    {
                        TargetTypeCode = t.TargetTypeCode.Trim(),
                        TargetValue = t.TargetValue.Trim()
                    });
            }
            else
            {
                note.Targets.Add(new AppNoteTarget
                {
                    TargetTypeCode = "ALL",
                    TargetValue = "*"
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

            note.Title = request.Title.Trim();
            note.NoteBody = request.NoteBody.Trim();
            note.NoteTypeCode = request.NoteTypeCode.Trim();
            note.SourceTypeCode = request.SourceTypeCode.Trim();
            note.CategoryCode = Norm(request.CategoryCode);
            note.PriorityCode = request.PriorityCode.Trim();
            note.VisibilityTypeCode = request.VisibilityTypeCode.Trim();
            // 🌟 TYPO FIXED: Yahan comma laga hua tha jisko ab semicolon ( ; ) kar dia hai
            note.MenuCode = Norm(request.MenuCode);
            note.ModuleName = Norm(request.ModuleName);
            note.EntityType = Norm(request.EntityType);
            note.EntityId = Norm(request.EntityId);
            note.StartDateUtc = request.StartDateUtc;
            note.EndDateUtc = request.EndDateUtc;
            note.IsPublished = request.IsPublished;
            note.IsPinned = request.IsPinned;
            note.IsPopup = request.IsPopup;
            note.IsBanner = request.IsBanner;
            note.RequireAcknowledgement = request.RequireAcknowledgement;
            note.AllowDismiss = request.AllowDismiss;
            if (note.SourceTypeCode == "USER" && string.IsNullOrWhiteSpace(note.OwnerIdentityUserId))
                note.OwnerIdentityUserId = updatedByUserId;
            note.UpdatedBy = updatedByUserId;
            note.UpdatedOnUtc = DateTime.UtcNow;

            var oldTargets = await _db.AppNoteTargets.Where(t => t.NoteId == noteId).ToListAsync(ct);
            _db.AppNoteTargets.RemoveRange(oldTargets);

            if (note.SourceTypeCode == "USER")
            {
            }
            else if (request.Targets != null && request.Targets.Count > 0)
            {
                foreach (var t in request.Targets)
                    note.Targets.Add(new AppNoteTarget
                    {
                        TargetTypeCode = t.TargetTypeCode.Trim(),
                        TargetValue = t.TargetValue.Trim()
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
            note.IsDeleted = true;
            note.DeletedBy = deletedByUserId;
            note.DeletedOnUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("AppNote deleted. NoteId={NoteId} By={UserId}", noteId, deletedByUserId);
        }

        // ── Mark Read ─────────────────────────────────────────────────────────

        public async Task MarkReadAsync(int noteId, string staffId, CancellationToken ct)
        {
            var state = await GetOrCreateStateAsync(noteId, staffId, ct);
            state.IsRead = true;
            state.ReadOnUtc ??= DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        // ── Acknowledge ───────────────────────────────────────────────────────

        public async Task AcknowledgeAsync(int noteId, string staffId, CancellationToken ct)
        {
            var state = await GetOrCreateStateAsync(noteId, staffId, ct);
            state.IsRead = true;
            state.ReadOnUtc ??= DateTime.UtcNow;
            state.IsAcknowledged = true;
            state.AcknowledgedOnUtc ??= DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        // ── Dismiss ───────────────────────────────────────────────────────────

        public async Task DismissAsync(int noteId, string staffId, CancellationToken ct)
        {
            var note = await LoadNoteAsync(noteId, ct);
            if (!note.AllowDismiss)
                throw new InvalidOperationException("This note cannot be dismissed.");

            var state = await GetOrCreateStateAsync(noteId, staffId, ct);
            state.IsDismissed = true;
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

        public async Task<List<AppNoteDto>> GetLoginInstructionsAsync(
            string staffId, string identityUserId, CancellationToken ct)
        {
            var visible = await GetVisibleAsync(staffId, identityUserId, null, null, null, ct);
            return visible
                .Where(n => n.SourceTypeCode == "ADMIN")
                .OrderByDescending(n => n.IsPinned)
                .ThenByDescending(n => n.CreatedOnUtc)
                .ToList();
        }

        public async Task<List<AdminInstructionDto>> GetAdminInstructionsAsync(CancellationToken ct)
        {
            var notes = await _db.AppNotes
                .AsNoTracking()
                .Include(n => n.Targets)
                .Where(n => n.SourceTypeCode == "ADMIN" && !n.IsDeleted)
                .OrderByDescending(n => n.CreatedOnUtc)
                .ToListAsync(ct);

            return notes.Select(n => new AdminInstructionDto
            {
                NoteId = n.NoteId,
                Title = n.Title,
                NoteBody = n.NoteBody,
                NoteTypeCode = n.NoteTypeCode,
                SourceTypeCode = n.SourceTypeCode,
                CategoryCode = n.CategoryCode,
                PriorityCode = n.PriorityCode,
                VisibilityTypeCode = n.VisibilityTypeCode,
                MenuCode = n.MenuCode,
                ModuleName = n.ModuleName,
                EntityType = n.EntityType,
                EntityId = n.EntityId,
                IsPublished = n.IsPublished,
                IsPinned = n.IsPinned,
                IsPopup = n.IsPopup,
                IsBanner = n.IsBanner,
                RequireAcknowledgement = n.RequireAcknowledgement,
                AllowDismiss = n.AllowDismiss,
                IsReadOnly = true,
                IsActive = n.IsActive,
                StartDateUtc = n.StartDateUtc,
                EndDateUtc = n.EndDateUtc,
                CreatedBy = n.CreatedBy,
                CreatedOnUtc = n.CreatedOnUtc,
                Targets = n.Targets.Select(t => new AppNoteTargetRequest
                {
                    TargetTypeCode = t.TargetTypeCode,
                    TargetValue = t.TargetValue
                }).ToList()
            }).ToList();
        }

        // ── Private helpers ───────────────────────────────────────────────────

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
            var normalizedStaffId = NormalizeIdentifier(staffId);

            var state = await _db.AppNoteUserStates
                .FirstOrDefaultAsync(s => s.NoteId == noteId && s.StaffId == normalizedStaffId, ct);

            if (state != null) return state;

            state = new AppNoteUserState { NoteId = noteId, StaffId = normalizedStaffId };
            _db.AppNoteUserStates.Add(state);
            return state;
        }

        private static AppNoteDto ToDto(AppNote note, AppNoteUserState? state)
        {
            return new AppNoteDto
            {
                NoteId = note.NoteId,
                Title = note.Title,
                NoteBody = note.NoteBody,
                NoteTypeCode = note.NoteTypeCode,
                SourceTypeCode = note.SourceTypeCode,
                CategoryCode = note.CategoryCode,
                PriorityCode = note.PriorityCode,
                VisibilityTypeCode = note.VisibilityTypeCode,
                MenuCode = note.MenuCode,
                ModuleName = note.ModuleName,
                EntityType = note.EntityType,
                EntityId = note.EntityId,
                IsPublished = note.IsPublished,
                IsPinned = note.IsPinned,
                IsPopup = note.IsPopup,
                IsBanner = note.IsBanner,
                RequireAcknowledgement = note.RequireAcknowledgement,
                AllowDismiss = note.AllowDismiss,
                IsRead = state?.IsRead ?? false,
                IsAcknowledged = state?.IsAcknowledged ?? false,
                IsDismissed = state?.IsDismissed ?? false,
                IsReadOnly = note.SourceTypeCode == "ADMIN",
                CreatedBy = note.CreatedBy,
                CreatedOnUtc = note.CreatedOnUtc
            };
        }

        private static string NormalizeIdentifier(string? value)
        {
            return (value ?? string.Empty).Trim().ToLowerInvariant();
        }

        private static void AddIdentifier(ISet<string> identifiers, string? value)
        {
            var normalized = NormalizeIdentifier(value);
            if (!string.IsNullOrWhiteSpace(normalized))
                identifiers.Add(normalized);
        }

        private static int PriorityRank(string code) => code switch
        {
            "CRITICAL" => 1,
            "HIGH" => 2,
            "NORMAL" => 3,
            "LOW" => 4,
            _ => 9
        };

        private static void Validate(CreateAppNoteRequest r)
        {
            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(r.Title)) errors.Add("Title is required.");
            if (string.IsNullOrWhiteSpace(r.NoteBody)) errors.Add("Note body is required.");
            if (string.IsNullOrWhiteSpace(r.NoteTypeCode)) errors.Add("Note type is required.");
            if (string.IsNullOrWhiteSpace(r.SourceTypeCode)) errors.Add("Source type is required.");
            if (string.IsNullOrWhiteSpace(r.PriorityCode)) errors.Add("Priority is required.");
            if (string.IsNullOrWhiteSpace(r.VisibilityTypeCode)) errors.Add("Visibility type is required.");
            if (r.StartDateUtc.HasValue && r.EndDateUtc.HasValue && r.EndDateUtc < r.StartDateUtc)
                errors.Add("End date cannot be before start date.");

            if (r.SourceTypeCode.Equals("ADMIN", StringComparison.OrdinalIgnoreCase) &&
                r.VisibilityTypeCode.Equals("STAFF", StringComparison.OrdinalIgnoreCase) &&
                r.Targets != null)
            {
                foreach (var t in r.Targets.Where(t =>
                    t.TargetTypeCode.Equals("STAFF", StringComparison.OrdinalIgnoreCase)))
                {
                    if (!Guid.TryParse(t.TargetValue?.Trim(), out _))
                        errors.Add($"Invalid staff target '{t.TargetValue}'. Use StaffVacancy.StaffId (GUID), not a numeric id.");
                }
            }

            if (errors.Any())
                throw new ArgumentException(string.Join(" | ", errors));
        }

        private static string? Norm(string? v) =>
            string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    }
}
