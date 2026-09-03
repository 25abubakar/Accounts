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
            try
            {
                var now = PakistanClock.Now();
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

            // ── Resolve Creator Profiles ──
            var creatorIds = notes
                .Where(n => !string.IsNullOrWhiteSpace(n.CreatedBy))
                .Select(n => n.CreatedBy!)
                .Distinct()
                .ToList();

            var creatorMap = new Dictionary<string, (string Name, string Photo)>();
            if (creatorIds.Any())
            {
                var creators = await _db.Persons
                    .AsNoTracking()
                    .Where(p => creatorIds.Contains(p.IdentityUserId))
                    .Select(p => new { p.IdentityUserId, p.FirstName, p.MiddleName, p.LastName, p.ProfilePhotoUrl })
                    .ToListAsync(ct);

                foreach (var c in creators)
                {
                    var fullName = string.Join(" ", new[] { c.FirstName, c.MiddleName, c.LastName }.Where(s => !string.IsNullOrWhiteSpace(s)));
                    creatorMap[c.IdentityUserId] = (fullName, c.ProfilePhotoUrl ?? "");
                }

                // ── Fallback for Admins not in Persons table ──
                var missingIds = creatorIds.Except(creatorMap.Keys).ToList();
                if (missingIds.Any())
                {
                    var fallbackUsers = await _db.Users
                        .AsNoTracking()
                        .Where(u => missingIds.Contains(u.Id))
                        .Select(u => new { u.Id, u.UserName, u.Email })
                        .ToListAsync(ct);

                    foreach (var u in fallbackUsers)
                    {
                        var displayName = !string.IsNullOrWhiteSpace(u.UserName) ? u.UserName : u.Email;
                        if (!string.IsNullOrWhiteSpace(displayName) && displayName.Contains("@")) 
                        {
                            displayName = displayName.Split('@')[0];
                            // Optional: capitalize first letter of email prefix
                            if (displayName.Length > 0)
                                displayName = char.ToUpper(displayName[0]) + displayName.Substring(1);
                        }
                        creatorMap[u.Id] = (displayName ?? "Administration", "");
                    }
                }
            }

            // Step 4: exclude dismissed, map to DTOs
            return notes
                .Where(n => !stateMap.TryGetValue(n.NoteId, out var st) || !st.IsDismissed)
                .Select(n =>
                {
                    stateMap.TryGetValue(n.NoteId, out var state);
                    string? creatorName = null;
                    string? creatorPhoto = null;
                    if (n.CreatedBy != null && creatorMap.TryGetValue(n.CreatedBy, out var info))
                    {
                        creatorName = info.Name;
                        creatorPhoto = info.Photo;
                    }
                    return ToDto(n, state, creatorName, creatorPhoto);
                })
                .ToList();
            }
            catch (OperationCanceledException)
            {
                return new List<AppNoteDto>();
            }
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
            var targetScope = await GetInstructionAudienceScopeAsync(createdByUserId, ct);
            var targets = NormalizeInstructionTargets(request, targetScope);

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
                CreatedOnUtc = PakistanClock.Now()
            };

            if (note.SourceTypeCode == "USER")
            {
            }
            else if (targets.Count > 0)
            {
                foreach (var t in targets)
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
            var targetScope = await GetInstructionAudienceScopeAsync(updatedByUserId, ct);
            if (!targetScope.CanBroadcastToEveryone &&
                !string.Equals(note.CreatedBy, updatedByUserId, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("You can only update instructions created by you.");
            var targets = NormalizeInstructionTargets(request, targetScope);

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
            note.UpdatedOnUtc = PakistanClock.Now();

            var oldTargets = await _db.AppNoteTargets.Where(t => t.NoteId == noteId).ToListAsync(ct);
            _db.AppNoteTargets.RemoveRange(oldTargets);

            if (note.SourceTypeCode == "USER")
            {
            }
            else if (targets.Count > 0)
            {
                foreach (var t in targets)
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
            note.DeletedOnUtc = PakistanClock.Now();
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("AppNote deleted. NoteId={NoteId} By={UserId}", noteId, deletedByUserId);
        }

        // ── Mark Read ─────────────────────────────────────────────────────────

        public async Task MarkReadAsync(int noteId, string staffId, CancellationToken ct)
        {
            var state = await GetOrCreateStateAsync(noteId, staffId, ct);
            state.IsRead = true;
            state.ReadOnUtc ??= PakistanClock.Now();
            await _db.SaveChangesAsync(ct);
        }

        // ── Acknowledge ───────────────────────────────────────────────────────

        public async Task AcknowledgeAsync(int noteId, string staffId, CancellationToken ct)
        {
            var state = await GetOrCreateStateAsync(noteId, staffId, ct);
            state.IsRead = true;
            state.ReadOnUtc ??= PakistanClock.Now();
            state.IsAcknowledged = true;
            state.AcknowledgedOnUtc ??= PakistanClock.Now();
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
            state.DismissedOnUtc ??= PakistanClock.Now();
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

        public async Task<InstructionAudienceScopeDto> GetInstructionAudienceScopeAsync(string identityUserId, CancellationToken ct)
        {
            var user = await _db.Users.AsNoTracking()
                .Where(u => u.Id == identityUserId)
                .Select(u => new
                {
                    u.Id,
                    u.TenantId,
                    u.IsSuperAdmin,
                    u.IsTenantAdmin
                })
                .FirstOrDefaultAsync(ct);

            var fullScope = _tenantService.IsSuperAdmin ||
                            user?.IsSuperAdmin == true ||
                            user?.IsTenantAdmin == true;

            if (_tenantService.IsSuperAdmin || user?.IsSuperAdmin == true)
            {
                var tenantAdminRows = await (
                    from admin in _db.Users.AsNoTracking()
                    where admin.IsTenantAdmin && !admin.IsSuperAdmin
                    join tenant in _db.Tenants.AsNoTracking()
                        on admin.TenantId equals tenant.Id into tenantJoin
                    from tenant in tenantJoin.DefaultIfEmpty()
                    orderby tenant != null ? tenant.TenantName : admin.UserName
                    select new
                    {
                        admin.Id,
                        admin.UserName,
                        admin.Email,
                        admin.TenantId,
                        TenantName = tenant != null ? tenant.TenantName : null,
                        OrganizationTreeId = tenant != null ? (int?)tenant.OrganizationTreeId : null
                    })
                    .ToListAsync(ct);

                var tenantAdmins = tenantAdminRows
                    .Select(admin => new InstructionTargetStaffDto
                    {
                        TargetId = admin.Id,
                        StaffId = Guid.Empty,
                        PersonId = Guid.Empty,
                        FullName = admin.UserName ?? admin.Email ?? admin.Id,
                        LoginId = admin.UserName,
                        JobTitle = "Tenant Admin",
                        Department = "Administration",
                        BranchName = null,
                        CompanyName = admin.TenantName,
                        CountryName = null,
                        OrganizationId = admin.OrganizationTreeId,
                        TenantId = admin.TenantId
                    })
                    .ToList();

                return new InstructionAudienceScopeDto
                {
                    CanBroadcastToEveryone = true,
                    ScopeLabel = "Tenant admins",
                    Staff = tenantAdmins
                };
            }

            var caller = await _db.Persons.AsNoTracking()
                .Where(person => person.IdentityUserId == identityUserId && person.IsActive)
                .Select(person => new
                {
                    person.PersonId,
                    person.TenantId,
                    StaffId = person.Staff != null ? (Guid?)person.Staff.StaffId : null,
                    OrganizationId = person.Staff != null && person.Staff.Vacancy != null
                        ? (int?)person.Staff.Vacancy.OrganizationId
                        : null,
                    JobTitle = person.Staff != null && person.Staff.Vacancy != null
                        ? (person.Staff.Vacancy.DesignationNav != null
                            ? person.Staff.Vacancy.DesignationNav.Name
                            : person.Staff.Vacancy.JobTitle)
                        : null,
                    InstructionScope = person.Staff != null &&
                        person.Staff.Vacancy != null &&
                        person.Staff.Vacancy.DesignationNav != null
                            ? person.Staff.Vacancy.DesignationNav.AttendanceVisibilityScope
                            : AttendanceVisibilityScope.Self
                })
                .FirstOrDefaultAsync(ct);

            var tenantId = user?.TenantId ?? caller?.TenantId ?? _tenantService.TenantId;
            var peopleQuery = _db.Persons.AsNoTracking()
                .Where(person =>
                    person.Staff != null &&
                    person.IsActive &&
                    !_db.Users.Any(u => u.Id == person.IdentityUserId && (u.IsTenantAdmin || u.IsSuperAdmin)));

            if (!_tenantService.IsSuperAdmin || tenantId.HasValue)
                peopleQuery = peopleQuery.Where(person => person.TenantId == tenantId);

            var people = await peopleQuery
                .Select(person => new
                {
                    person.PersonId,
                    person.FullName,
                    person.TenantId,
                    person.Staff!.StaffId,
                    person.Staff.LoginId,
                    person.Staff.VacancyId,
                    OrganizationId = person.Staff.Vacancy != null ? (int?)person.Staff.Vacancy.OrganizationId : null,
                    JobTitle = person.Staff.Vacancy != null
                        ? (person.Staff.Vacancy.DesignationNav != null
                            ? person.Staff.Vacancy.DesignationNav.Name
                            : person.Staff.Vacancy.JobTitle)
                        : null,
                    Department = person.Staff.Vacancy != null ? person.Staff.Vacancy.Department : null
                })
                .ToListAsync(ct);

            var orgs = await _db.OrganizationTree.AsNoTracking()
                .Where(node => node.IsActive)
                .Select(node => new { node.Id, node.ParentId, node.Name, node.Label })
                .ToListAsync(ct);
            var orgById = orgs.ToDictionary(node => node.Id);

            var visiblePersonIds = new HashSet<Guid>();
            if (fullScope)
            {
                visiblePersonIds = people.Select(person => person.PersonId).ToHashSet();
            }
            else if (caller?.OrganizationId.HasValue == true)
            {
                var callerRank = InstructionRoleRank(caller.JobTitle);
                var derivedScope = callerRank switch
                {
                    >= 300 => AttendanceVisibilityScope.OrganizationNodeAndDescendants,
                    >= 200 => AttendanceVisibilityScope.OrganizationNode,
                    _ => AttendanceVisibilityScope.Self
                };
                var effectiveScope = (AttendanceVisibilityScope)Math.Max(
                    (int)caller.InstructionScope,
                    (int)derivedScope);

                if (effectiveScope != AttendanceVisibilityScope.Self && callerRank >= 200)
                {
                    var visibleNodeIds = new HashSet<int> { caller.OrganizationId.Value };

                    if (effectiveScope == AttendanceVisibilityScope.OrganizationNodeAndDescendants)
                    {
                        var nodeChildren = orgs
                            .Where(node => node.ParentId.HasValue)
                            .ToLookup(node => node.ParentId!.Value, node => node.Id);
                        var pendingNodes = new Queue<int>();
                        pendingNodes.Enqueue(caller.OrganizationId.Value);
                        while (pendingNodes.TryDequeue(out var parentNodeId))
                            foreach (var childNodeId in nodeChildren[parentNodeId])
                                if (visibleNodeIds.Add(childNodeId)) pendingNodes.Enqueue(childNodeId);
                    }

                    foreach (var person in people)
                        if (person.OrganizationId.HasValue &&
                            visibleNodeIds.Contains(person.OrganizationId.Value) &&
                            InstructionRoleRank(person.JobTitle) < callerRank)
                            visiblePersonIds.Add(person.PersonId);
                }
            }

            string? FindOrgName(int? organizationId, string label)
            {
                var currentId = organizationId;
                while (currentId.HasValue && orgById.TryGetValue(currentId.Value, out var node))
                {
                    if (string.Equals(node.Label, label, StringComparison.OrdinalIgnoreCase))
                        return node.Name;
                    currentId = node.ParentId;
                }
                return null;
            }

            var staff = people
                .Where(person => visiblePersonIds.Contains(person.PersonId))
                .OrderBy(person => person.FullName)
                .Select(person => new InstructionTargetStaffDto
                {
                    TargetId = person.StaffId.ToString(),
                    StaffId = person.StaffId,
                    PersonId = person.PersonId,
                    FullName = person.FullName,
                    LoginId = person.LoginId,
                    JobTitle = person.JobTitle,
                    Department = FindOrgName(person.OrganizationId, "Department") ?? person.Department,
                    BranchName = FindOrgName(person.OrganizationId, "Branch"),
                    CompanyName = FindOrgName(person.OrganizationId, "Company") ?? FindOrgName(person.OrganizationId, "Group"),
                    CountryName = FindOrgName(person.OrganizationId, "Country"),
                    OrganizationId = person.OrganizationId,
                    TenantId = person.TenantId
                })
                .ToList();

            return new InstructionAudienceScopeDto
            {
                CanBroadcastToEveryone = fullScope,
                ScopeLabel = fullScope ? "Company" : "Organization node hierarchy",
                Staff = staff
            };
        }

        public async Task<List<AdminInstructionDto>> GetAdminInstructionsAsync(string identityUserId, CancellationToken ct)
        {
            var normalizedCreatorId = NormalizeIdentifier(identityUserId);

            var notes = await _db.AppNotes
                .AsNoTracking()
                .Include(n => n.Targets)
                .Where(n => n.SourceTypeCode == "ADMIN" && !n.IsDeleted)
                .Where(n => n.CreatedBy != null && n.CreatedBy.ToLower() == normalizedCreatorId)
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

        private static List<AppNoteTargetRequest> NormalizeInstructionTargets(
            CreateAppNoteRequest request,
            InstructionAudienceScopeDto scope)
        {
            if (!request.SourceTypeCode.Trim().Equals("ADMIN", StringComparison.OrdinalIgnoreCase))
                return new List<AppNoteTargetRequest>();

            var requestedTargets = request.Targets ?? new List<AppNoteTargetRequest>();
            var allowAll = scope.CanBroadcastToEveryone;
            var allowedStaffIds = scope.Staff
                .Select(staff => NormalizeIdentifier(string.IsNullOrWhiteSpace(staff.TargetId)
                    ? staff.StaffId.ToString()
                    : staff.TargetId))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (requestedTargets.Count == 0)
            {
                if (allowAll) return new List<AppNoteTargetRequest> { new() { TargetTypeCode = "ALL", TargetValue = "*" } };
                throw new UnauthorizedAccessException("Select at least one staff member from your organization hierarchy.");
            }

            var normalized = new List<AppNoteTargetRequest>();
            foreach (var target in requestedTargets)
            {
                var type = target.TargetTypeCode.Trim().ToUpperInvariant();
                var value = target.TargetValue.Trim();

                if (type == "ALL")
                {
                    if (!allowAll)
                        throw new UnauthorizedAccessException("Your account can only send instructions to staff in your organization hierarchy.");
                    normalized.Add(new AppNoteTargetRequest { TargetTypeCode = "ALL", TargetValue = "*" });
                    continue;
                }

                if (type == "STAFF")
                {
                    if (!allowedStaffIds.Contains(NormalizeIdentifier(value)))
                        throw new UnauthorizedAccessException("One or more selected staff members are outside your instruction scope.");
                    normalized.Add(new AppNoteTargetRequest { TargetTypeCode = "STAFF", TargetValue = value });
                    continue;
                }

                if (type is "MENU" or "RECORD")
                {
                    normalized.Add(new AppNoteTargetRequest { TargetTypeCode = type, TargetValue = value });
                    continue;
                }

                throw new InvalidOperationException($"Unsupported instruction target type: {target.TargetTypeCode}");
            }

            if (!allowAll && !normalized.Any(target => target.TargetTypeCode == "STAFF"))
                throw new UnauthorizedAccessException("Select at least one staff member from your organization hierarchy.");

            return normalized
                .GroupBy(target => $"{target.TargetTypeCode}:{NormalizeIdentifier(target.TargetValue)}")
                .Select(group => group.First())
                .ToList();
        }

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

        private static AppNoteDto ToDto(AppNote note, AppNoteUserState? state, string? creatorName = null, string? creatorPhotoUrl = null)
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
                CreatorName = creatorName,
                CreatorPhotoUrl = creatorPhotoUrl,
                CreatedOnUtc = note.CreatedOnUtc,
                StartDateUtc = note.StartDateUtc,
                EndDateUtc = note.EndDateUtc
            };
        }

        private static string NormalizeIdentifier(string? value)
        {
            return (value ?? string.Empty).Trim().ToLowerInvariant();
        }

        private static int InstructionRoleRank(string? title)
        {
            if (string.IsNullOrWhiteSpace(title)) return 0;
            var value = new string(title.Trim().ToLowerInvariant()
                .Where(char.IsLetterOrDigit).ToArray());

            var isDutyCeo = value.Contains("dutyceo");
            var isDeputyManager = value.Contains("deputymanager") || value.Contains("deptymanager");
            var isAssistantManager = value.Contains("assistantmanager") ||
                                     value.Contains("asstmanager") ||
                                     value.Contains("assistmanager");

            if (!isDutyCeo && (value.Contains("ceo") || value.Contains("chiefexecutive"))) return 700;
            if (isDutyCeo) return 600;
            if (!isDeputyManager && !isAssistantManager && value.Contains("manager")) return 500;
            if (isDeputyManager) return 400;
            if (isAssistantManager) return 300;
            if (value.Contains("supervisor") || value.Contains("teamlead")) return 200;
            if (value.Contains("agent") || value.Contains("bellboy")) return 100;
            return 0;
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
                    if (string.IsNullOrWhiteSpace(t.TargetValue))
                        errors.Add("Instruction staff target is required.");
                }
            }

            if (errors.Any())
                throw new ArgumentException(string.Join(" | ", errors));
        }

        private static string? Norm(string? v) =>
            string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    }
}
