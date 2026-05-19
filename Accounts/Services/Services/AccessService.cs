using Accounts.Data;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Services
{
    public class AccessService : IAccessService
    {
        private readonly ApplicationDbContext _db;

        public AccessService(ApplicationDbContext db) => _db = db;

        public async Task<IEnumerable<object>> GetAllFeaturesAsync() =>
            await _db.Features
                .OrderBy(f => f.Module).ThenBy(f => f.FeatureKey)
                .Select(f => new { f.FeatureKey, f.FeatureName, f.Module, f.Description })
                .ToListAsync<object>();

        public async Task<IEnumerable<object>> GetFeaturesByModuleAsync(string module) =>
            await _db.Features
                .Where(f => f.Module.ToLower() == module.ToLower())
                .OrderBy(f => f.FeatureKey)
                .Select(f => new { f.FeatureKey, f.FeatureName, f.Module, f.Description })
                .ToListAsync<object>();


        public async Task<IEnumerable<object>> GetAllGroupsAsync() =>
            await _db.AccessGroups
                .Include(g => g.Features)
                .Where(g => g.IsActive)
                .OrderBy(g => g.GroupName)
                .Select(g => new
                {
                    g.GroupId, g.GroupName, g.Description, g.IsActive, g.CreatedDate,
                    Features = g.Features.Select(f => f.FeatureKey).ToList(),
                    StaffCount = g.Staff.Count()
                })
                .ToListAsync<object>();

        public async Task<object?> GetGroupByIdAsync(int groupId)
        {
            var g = await _db.AccessGroups
                .Include(x => x.Features)
                .Include(x => x.Staff).ThenInclude(s => s.Staff)
                .FirstOrDefaultAsync(x => x.GroupId == groupId);
            if (g == null) return null;

            return new
            {
                g.GroupId, g.GroupName, g.Description, g.IsActive, g.CreatedDate,
                Features = g.Features.Select(f => f.FeatureKey).ToList(),
                Staff    = g.Staff.Select(s => new { s.StaffId, s.Staff!.FullName, s.AssignedDate, s.Note }).ToList()
            };
        }

        public async Task<AccessGroup> CreateGroupAsync(string groupName, string? description)
        {
            var group = new AccessGroup { GroupName = groupName, Description = description };
            _db.AccessGroups.Add(group);
            await _db.SaveChangesAsync();
            return group;
        }

        public async Task<bool> UpdateGroupAsync(int groupId, string groupName, string? description)
        {
            var group = await _db.AccessGroups.FindAsync(groupId);
            if (group == null) return false;
            group.GroupName   = groupName;
            group.Description = description;
            await _db.SaveChangesAsync();
            return true;
        }

        public async Task<bool> DeleteGroupAsync(int groupId)
        {
            var group = await _db.AccessGroups.FindAsync(groupId);
            if (group == null) return false;
            group.IsActive = false;
            await _db.SaveChangesAsync();
            return true;
        }


        public async Task<(bool Success, string Message)> SetGroupFeaturesAsync(
            int groupId, IEnumerable<string> featureKeys)
        {
            var group = await _db.AccessGroups.FindAsync(groupId);
            if (group == null) return (false, $"Group {groupId} not found.");

            // Load valid keys to avoid FK violations
            var validKeys = await _db.Features
                .Select(f => f.FeatureKey)
                .ToHashSetAsync();

            var existing = await _db.AccessGroupFeatures
                .Where(x => x.GroupId == groupId).ToListAsync();
            _db.AccessGroupFeatures.RemoveRange(existing);

            int added = 0;
            foreach (var key in featureKeys.Distinct())
            {
                if (validKeys.Contains(key))
                {
                    _db.AccessGroupFeatures.Add(new AccessGroupFeature { GroupId = groupId, FeatureKey = key });
                    added++;
                }
            }

            await _db.SaveChangesAsync();
            return (true, $"{added} features assigned to group.");
        }


        public async Task<(bool Success, string Message)> AssignGroupToStaffAsync(
            Guid staffId, int groupId, string? assignedBy, string? note)
        {
            if (!await _db.Staff.AnyAsync(s => s.StaffId == staffId))
                return (false, $"Staff {staffId} not found.");
            if (!await _db.AccessGroups.AnyAsync(g => g.GroupId == groupId))
                return (false, $"Group {groupId} not found.");
            if (await _db.StaffAccessGroups.AnyAsync(x => x.StaffId == staffId && x.GroupId == groupId))
                return (false, "Staff is already in this group.");

            _db.StaffAccessGroups.Add(new StaffAccessGroup
            {
                StaffId      = staffId,
                GroupId      = groupId,
                AssignedBy   = assignedBy,
                AssignedDate = DateTime.Now,
                Note         = note
            });
            await _db.SaveChangesAsync();
            return (true, "Group assigned to staff.");
        }

        public async Task<(bool Success, string Message)> RemoveGroupFromStaffAsync(Guid staffId, int groupId)
        {
            var link = await _db.StaffAccessGroups
                .FirstOrDefaultAsync(x => x.StaffId == staffId && x.GroupId == groupId);
            if (link == null) return (false, "Assignment not found.");
            _db.StaffAccessGroups.Remove(link);
            await _db.SaveChangesAsync();
            return (true, "Group removed from staff.");
        }

        public async Task<IEnumerable<object>> GetStaffGroupsAsync(Guid staffId) =>
            await _db.StaffAccessGroups
                .Include(x => x.Group)
                .Where(x => x.StaffId == staffId)
                .Select(x => new { x.GroupId, x.Group!.GroupName, x.AssignedDate, x.Note })
                .ToListAsync<object>();


        public async Task<object> GetDepartmentMatrixAsync(int deptId)
        {
            // ── 1. All features ───────────────────────────────────────────────
            var features = await _db.Features
                .OrderBy(f => f.Module).ThenBy(f => f.FeatureKey)
                .ToListAsync();

            // ── 2. Source A: Persons registered in this branch (BranchId) ────
            var personsInDept = await _db.Persons
                .Include(p => p.Staff).ThenInclude(s => s!.Vacancy)
                .Where(p => p.BranchId == deptId)
                .OrderBy(p => p.FullName)
                .ToListAsync();

            // ── 3. Source B: Staff whose vacancy is in this dept ──────────────
            // Load ALL staff for this dept first, then filter in memory
            var allStaffForDept = await _db.Staff
                .Include(s => s.Person)
                .Include(s => s.Vacancy)
                .Where(s => s.Vacancy != null && s.Vacancy.OrganizationId == deptId)
                .OrderBy(s => s.FullName)
                .ToListAsync();

            // Filter out staff already covered by Source A (in memory — no EF translation issue)
            var coveredPersonIds = personsInDept
                .Select(p => p.PersonId)
                .ToHashSet();

            var extraStaff = allStaffForDept
                .Where(s => s.PersonId == null || !coveredPersonIds.Contains(s.PersonId.Value))
                .ToList();

            // ── 4. Existing matrix rows ───────────────────────────────────────
            var matrix = await _db.DepartmentAccessMatrix
                .Where(m => m.DeptId == deptId)
                .ToListAsync();

            // ── 5. Build grid from persons ────────────────────────────────────
            var gridFromPersons = personsInDept.Select(p =>
            {
                var sid = p.Staff?.StaffId ?? Guid.Empty;
                return new
                {
                    staffId     = sid,
                    personId    = p.PersonId,
                    fullName    = p.FullName,
                    loginId     = p.LoginId,
                    jobTitle    = p.Staff?.Vacancy?.JobTitle ?? "-",
                    isHired     = p.Staff != null,
                    permissions = features.Select(f => new
                    {
                        featureKey  = f.FeatureKey,
                        featureName = f.FeatureName,
                        module      = f.Module,
                        hasAccess   = sid != Guid.Empty && matrix.Any(m =>
                                          m.StaffId == sid &&
                                          m.FeatureKey == f.FeatureKey &&
                                          m.HasAccess)
                    }).ToList()
                };
            }).ToList();

            // ── 6. Build grid from extra staff ────────────────────────────────
            var gridFromStaff = extraStaff.Select(s => new
            {
                staffId     = s.StaffId,
                personId    = s.PersonId,
                fullName    = s.FullName,
                loginId     = s.Person?.LoginId ?? "-",
                jobTitle    = s.Vacancy?.JobTitle ?? "-",
                isHired     = true,
                permissions = features.Select(f => new
                {
                    featureKey  = f.FeatureKey,
                    featureName = f.FeatureName,
                    module      = f.Module,
                    hasAccess   = matrix.Any(m =>
                                      m.StaffId == s.StaffId &&
                                      m.FeatureKey == f.FeatureKey &&
                                      m.HasAccess)
                }).ToList()
            }).ToList();

            var allStaff = gridFromPersons
                .Cast<object>()
                .Concat(gridFromStaff.Cast<object>())
                .ToList();

            return new
            {
                deptId,
                totalStaff = allStaff.Count,
                features   = features.Select(f => new { f.FeatureKey, f.FeatureName, f.Module }).ToList(),
                staff      = allStaff
            };
        }

        public async Task<(int Updated, string Message)> SaveDepartmentMatrixAsync(
            int deptId, IEnumerable<MatrixUpdateItem> items, string? grantedBy)
        {
            // Load all valid feature keys once — skip any item whose key doesn't exist
            var validKeys = await _db.Features
                .Select(f => f.FeatureKey)
                .ToHashSetAsync();

            int count = 0;
            var skipped = new List<string>();

            foreach (var item in items)
            {
                // Skip invalid feature keys — prevents FK_DAM_Feature violation
                if (!validKeys.Contains(item.FeatureKey))
                {
                    skipped.Add(item.FeatureKey);
                    continue;
                }

                var existing = await _db.DepartmentAccessMatrix
                    .FirstOrDefaultAsync(m => m.StaffId == item.StaffId
                                           && m.FeatureKey == item.FeatureKey);
                if (existing == null)
                {
                    _db.DepartmentAccessMatrix.Add(new DepartmentAccessMatrix
                    {
                        StaffId     = item.StaffId,
                        DeptId      = deptId,
                        FeatureKey  = item.FeatureKey,
                        HasAccess   = item.HasAccess,
                        GrantedBy   = grantedBy,
                        GrantedDate = DateTime.Now
                    });
                }
                else
                {
                    existing.HasAccess   = item.HasAccess;
                    existing.GrantedBy   = grantedBy;
                    existing.GrantedDate = DateTime.Now;
                }
                count++;
            }

            if (count > 0)
                await _db.SaveChangesAsync();

            var msg = skipped.Any()
                ? $"{count} permissions updated. Skipped {skipped.Count} unknown keys: {string.Join(", ", skipped)}"
                : $"{count} permissions updated.";

            return (count, msg);
        }

        public async Task<(bool Success, string Message)> TogglePermissionAsync(
            Guid staffId, string featureKey, bool hasAccess, string? grantedBy)
        {
            if (!await _db.Staff.AnyAsync(s => s.StaffId == staffId))
                return (false, $"Staff {staffId} not found.");
            if (!await _db.Features.AnyAsync(f => f.FeatureKey == featureKey))
                return (false, $"Feature '{featureKey}' not found. Valid keys: use GET /api/access/features.");

            var existing = await _db.DepartmentAccessMatrix
                .FirstOrDefaultAsync(m => m.StaffId == staffId && m.FeatureKey == featureKey);

            if (existing == null)
            {
                var staff = await _db.Staff.Include(s => s.Vacancy)
                    .FirstOrDefaultAsync(s => s.StaffId == staffId);
                int deptId = staff?.Vacancy?.OrganizationId ?? 0;

                _db.DepartmentAccessMatrix.Add(new DepartmentAccessMatrix
                {
                    StaffId     = staffId,
                    DeptId      = deptId,
                    FeatureKey  = featureKey,
                    HasAccess   = hasAccess,
                    GrantedBy   = grantedBy,
                    GrantedDate = DateTime.Now
                });
            }
            else
            {
                existing.HasAccess   = hasAccess;
                existing.GrantedBy   = grantedBy;
                existing.GrantedDate = DateTime.Now;
            }

            await _db.SaveChangesAsync();
            return (true, $"Permission '{featureKey}' {(hasAccess ? "granted" : "revoked")}.");
        }

        public async Task<(int Count, string Message)> GrantAllAsync(
            Guid staffId, int deptId, string? grantedBy)
        {
            // Get staff's dept if deptId not provided
            if (deptId <= 0)
            {
                var s = await _db.Staff.Include(x => x.Vacancy)
                    .FirstOrDefaultAsync(x => x.StaffId == staffId);
                deptId = s?.Vacancy?.OrganizationId ?? 0;
            }

            var features = await _db.Features.ToListAsync();
            var items    = features.Select(f => new MatrixUpdateItem
            {
                StaffId    = staffId,
                FeatureKey = f.FeatureKey,
                HasAccess  = true
            });
            var (count, _) = await SaveDepartmentMatrixAsync(deptId, items, grantedBy);
            return (count, $"All {count} permissions granted.");
        }

        public async Task<(int Count, string Message)> RevokeAllAsync(
            Guid staffId, string? grantedBy)
        {
            var rows = await _db.DepartmentAccessMatrix
                .Where(m => m.StaffId == staffId).ToListAsync();
            foreach (var r in rows)
            {
                r.HasAccess   = false;
                r.GrantedBy   = grantedBy;
                r.GrantedDate = DateTime.Now;
            }
            await _db.SaveChangesAsync();
            return (rows.Count, $"All {rows.Count} permissions revoked.");
        }

        public async Task<IEnumerable<string>> GetStaffPermissionsAsync(Guid staffId)
        {
            var matrixPerms = await _db.DepartmentAccessMatrix
                .Where(m => m.StaffId == staffId && m.HasAccess)
                .Select(m => m.FeatureKey)
                .ToListAsync();

            var groupPerms = await _db.StaffAccessGroups
                .Where(s => s.StaffId == staffId)
                .SelectMany(s => s.Group!.Features.Select(f => f.FeatureKey))
                .ToListAsync();

            return matrixPerms.Union(groupPerms).Distinct().OrderBy(k => k);
        }

        public async Task<IEnumerable<object>> GetDepartmentPersonsAsync(int deptId)
        {
            // All persons registered in this branch/department
            var persons = await _db.Persons
                .Include(p => p.Staff).ThenInclude(s => s!.Vacancy)
                .Where(p => p.BranchId == deptId)
                .OrderBy(p => p.FullName)
                .ToListAsync();

            return persons.Select(p => (object)new
            {
                personId   = p.PersonId,
                staffId    = p.Staff?.StaffId,
                fullName   = p.FullName,
                loginId    = p.LoginId,
                email      = p.Email,
                photoUrl   = p.ProfilePhotoUrl,
                isHired    = p.Staff != null,
                jobTitle   = p.Staff?.Vacancy?.JobTitle,
                vacancyCode = p.Staff?.Vacancy?.VacancyCode
            });
        }

        // ── GetEffectiveAccess ────────────────────────────────────────────────

        /// <summary>
        /// Merges access from DepartmentAccessMatrix (individual) and
        /// AccessGroupFeatures (group) for a specific staff + group combination.
        ///
        /// Priority rule: Individual OR Group — if EITHER grants access, HasAccess = true.
        /// Source field tells you exactly where the access came from.
        /// </summary>
        public async Task<EffectiveAccessResult> GetEffectiveAccessAsync(Guid staffId, int groupId)
        {
            // ── 1. Validate staff exists ──────────────────────────────────────
            var staff = await _db.Staff
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.StaffId == staffId)
                ?? throw new KeyNotFoundException($"Staff {staffId} not found.");

            // ── 2. Validate group exists ──────────────────────────────────────
            var group = await _db.AccessGroups
                .AsNoTracking()
                .FirstOrDefaultAsync(g => g.GroupId == groupId)
                ?? throw new KeyNotFoundException($"Group {groupId} not found.");

            // ── 3. Load all features ──────────────────────────────────────────
            var allFeatures = await _db.Features
                .AsNoTracking()
                .OrderBy(f => f.Module).ThenBy(f => f.FeatureKey)
                .ToListAsync();

            // ── 4. Load individual matrix rows for this staff ─────────────────
            // HashSet for O(1) lookup
            var individualAccess = await _db.DepartmentAccessMatrix
                .AsNoTracking()
                .Where(m => m.StaffId == staffId && m.HasAccess)
                .Select(m => m.FeatureKey)
                .ToHashSetAsync();

            // ── 5. Load group feature keys ────────────────────────────────────
            var groupAccess = await _db.AccessGroupFeatures
                .AsNoTracking()
                .Where(f => f.GroupId == groupId)
                .Select(f => f.FeatureKey)
                .ToHashSetAsync();

            // ── 6. Merge — feature is accessible if EITHER source grants it ───
            var mergedFeatures = allFeatures.Select(f => new EffectiveFeatureAccess
            {
                FeatureKey       = f.FeatureKey,
                FeatureName      = f.FeatureName,
                Module           = f.Module,
                IndividualAccess = individualAccess.Contains(f.FeatureKey),
                GroupAccess      = groupAccess.Contains(f.FeatureKey)
                // HasAccess and Source are computed properties — no assignment needed
            }).ToList();

            return new EffectiveAccessResult
            {
                StaffId   = staffId,
                GroupId   = groupId,
                StaffName = staff.FullName,
                GroupName = group.GroupName,
                Features  = mergedFeatures
            };
        }

        // ── SyncGroupToDeptMatrix ─────────────────────────────────────────────

        /// <summary>
        /// When a group's features change, sync those features into DepartmentAccessMatrix
        /// for every staff member who belongs to that group.
        ///
        /// Behaviour:
        ///   - Features the group NOW has  → set HasAccess = true  in matrix
        ///   - Features the group REMOVED  → set HasAccess = false in matrix
        ///     (individual overrides are preserved — only group-sourced rows are touched)
        ///
        /// Transaction: all-or-nothing. If any staff member fails, the entire sync rolls back.
        /// </summary>
        public async Task<(bool Success, string Message, int StaffSynced, int PermissionsSynced)>
            SyncGroupToDeptMatrixAsync(int groupId, string? syncedBy = null)
        {
            // ── 1. Validate group ─────────────────────────────────────────────
            var group = await _db.AccessGroups
                .AsNoTracking()
                .FirstOrDefaultAsync(g => g.GroupId == groupId);

            if (group == null)
                return (false, $"Group {groupId} not found.", 0, 0);

            // ── 2. Load group's current feature keys ──────────────────────────
            var groupFeatureKeys = await _db.AccessGroupFeatures
                .AsNoTracking()
                .Where(f => f.GroupId == groupId)
                .Select(f => f.FeatureKey)
                .ToHashSetAsync();

            // ── 3. Load all staff who belong to this group ────────────────────
            var groupMembers = await _db.StaffAccessGroups
                .AsNoTracking()
                .Where(s => s.GroupId == groupId)
                .Select(s => new { s.StaffId, s.Staff!.FullName, s.Staff.Vacancy!.OrganizationId })
                .ToListAsync();

            if (!groupMembers.Any())
                return (true, $"Group '{group.GroupName}' has no members. Nothing to sync.", 0, 0);

            // ── 4. Load all valid feature keys to prevent FK violations ────────
            var validKeys = await _db.Features
                .AsNoTracking()
                .Select(f => f.FeatureKey)
                .ToHashSetAsync();

            // Only sync keys that actually exist in Features table
            var keysToSync = groupFeatureKeys.Where(k => validKeys.Contains(k)).ToHashSet();

            int staffSynced       = 0;
            int permissionsSynced = 0;

            // ── 5. Wrap everything in a transaction ───────────────────────────
            var strategy = _db.Database.CreateExecutionStrategy();

            try
            {
                await strategy.ExecuteAsync(async () =>
                {
                    await using var tx = await _db.Database.BeginTransactionAsync();
                    try
                    {
                        foreach (var member in groupMembers)
                        {
                            int deptId = member.OrganizationId ?? 0;

                            // Load existing matrix rows for this staff member
                            var existingRows = await _db.DepartmentAccessMatrix
                                .Where(m => m.StaffId == member.StaffId)
                                .ToListAsync();

                            var existingByKey = existingRows.ToDictionary(r => r.FeatureKey);

                            // ── Grant: features the group now has ─────────────
                            foreach (var key in keysToSync)
                            {
                                if (existingByKey.TryGetValue(key, out var row))
                                {
                                    // Row exists — update only if currently denied
                                    if (!row.HasAccess)
                                    {
                                        row.HasAccess   = true;
                                        row.GrantedBy   = syncedBy ?? $"GroupSync:{group.GroupName}";
                                        row.GrantedDate = DateTime.Now;
                                        permissionsSynced++;
                                    }
                                }
                                else
                                {
                                    // Row doesn't exist — create it
                                    _db.DepartmentAccessMatrix.Add(new DepartmentAccessMatrix
                                    {
                                        StaffId     = member.StaffId,
                                        DeptId      = deptId,
                                        FeatureKey  = key,
                                        HasAccess   = true,
                                        GrantedBy   = syncedBy ?? $"GroupSync:{group.GroupName}",
                                        GrantedDate = DateTime.Now
                                    });
                                    permissionsSynced++;
                                }
                            }

                            // ── Revoke: features the group no longer has ──────
                            // Only revoke rows that were granted BY this group sync
                            // (rows with GrantedBy containing the group name or "GroupSync")
                            // This preserves individual overrides set by admins directly
                            foreach (var row in existingRows)
                            {
                                if (!keysToSync.Contains(row.FeatureKey) && row.HasAccess)
                                {
                                    // Only revoke if this row was originally set by a group sync
                                    // (not an individual admin override)
                                    bool wasGroupGranted = row.GrantedBy != null &&
                                        (row.GrantedBy.StartsWith("GroupSync:") ||
                                         row.GrantedBy == syncedBy);

                                    if (wasGroupGranted)
                                    {
                                        row.HasAccess   = false;
                                        row.GrantedBy   = syncedBy ?? $"GroupSync:{group.GroupName}";
                                        row.GrantedDate = DateTime.Now;
                                        permissionsSynced++;
                                    }
                                }
                            }

                            staffSynced++;
                        }

                        await _db.SaveChangesAsync();
                        await tx.CommitAsync();
                    }
                    catch
                    {
                        await tx.RollbackAsync();
                        throw; // re-throw so outer try-catch catches it
                    }
                });

                return (
                    true,
                    $"Sync complete. {staffSynced} staff members updated, {permissionsSynced} permissions synced for group '{group.GroupName}'.",
                    staffSynced,
                    permissionsSynced
                );
            }
            catch (Exception ex)
            {
                return (
                    false,
                    $"Sync failed and was rolled back. Error: {ex.Message}",
                    0,
                    0
                );
            }
        }
    }
}
