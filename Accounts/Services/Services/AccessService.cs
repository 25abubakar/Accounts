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
                .Include(g => g.Features).ThenInclude(f => f.Feature)
                .Where(g => g.IsActive)
                .OrderBy(g => g.GroupName)
                .Select(g => new
                {
                    g.GroupId, g.GroupName, g.Description, g.IsActive, g.CreatedDate,
                    Features = g.Features
                        .Where(f => f.Feature != null)
                        .Select(f => f.Feature!.FeatureKey).ToList(),
                    StaffCount = g.Staff.Count()
                })
                .ToListAsync<object>();

        public async Task<object?> GetGroupByIdAsync(int groupId)
        {
            var g = await _db.AccessGroups
                .Include(x => x.Features).ThenInclude(f => f.Feature)
                .Include(x => x.Staff).ThenInclude(s => s.Staff)
                .FirstOrDefaultAsync(x => x.GroupId == groupId);
            if (g == null) return null;

            return new
            {
                g.GroupId, g.GroupName, g.Description, g.IsActive, g.CreatedDate,
                Features = g.Features
                    .Where(f => f.Feature != null)
                    .Select(f => f.Feature!.FeatureKey).ToList(),
                Staff = g.Staff.Select(s => new
                {
                    s.StaffId,
                    FullName = s.Staff!.Person != null ? s.Staff.Person.FullName : "-",
                    s.AssignedDate,
                    s.Note
                }).ToList()
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

            // Map FeatureKey strings to PermissionId integers
            var featureMap = await _db.Features
                .Where(f => featureKeys.Contains(f.FeatureKey))
                .ToDictionaryAsync(f => f.FeatureKey, f => f.PermissionId);

            var existing = await _db.AccessGroupFeatures
                .Where(x => x.GroupId == groupId).ToListAsync();
            _db.AccessGroupFeatures.RemoveRange(existing);

            int added = 0;
            foreach (var key in featureKeys.Distinct())
            {
                if (featureMap.TryGetValue(key, out int permId))
                {
                    _db.AccessGroupFeatures.Add(new AccessGroupFeature { GroupId = groupId, PermissionId = permId });
                    added++;
                }
            }

            await _db.SaveChangesAsync();
            return (true, $"{added} features assigned to group.");
        }


        public async Task<(bool Success, string Message)> AssignGroupToStaffAsync(
            Guid staffId, int groupId, string? assignedBy, string? note)
        {
            if (!await _db.StaffVacancies.AnyAsync(s => s.StaffId == staffId))
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

            // ── 2. Source A: Persons hired into vacancies in this dept ───────
            var personsInDept = await _db.Persons
                .Include(p => p.Staff).ThenInclude(s => s!.Vacancy)
                .Where(p => p.Staff != null && p.Staff.Vacancy != null && p.Staff.Vacancy.OrganizationId == deptId)
                .OrderBy(p => p.FullName)
                .ToListAsync();

            // ── 3. Source B: Staff whose vacancy is in this dept ──────────────
            var allStaffForDept = await _db.StaffVacancies
                .Include(s => s.Person)
                .Include(s => s.Vacancy)
                .Where(s => s.Vacancy != null && s.Vacancy.OrganizationId == deptId)
                .OrderBy(s => s.Person != null ? s.Person.FullName : "")
                .ToListAsync();

            var coveredPersonIds = personsInDept.Select(p => p.PersonId).ToHashSet();
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
                    loginId     = p.Staff?.LoginId,
                    jobTitle    = p.Staff?.Vacancy?.JobTitle ?? "-",
                    isHired     = p.Staff != null,
                    permissions = features.Select(f => new
                    {
                        featureKey  = f.FeatureKey,
                        featureName = f.FeatureName,
                        module      = f.Module,
                        hasAccess   = sid != Guid.Empty && matrix.Any(m =>
                                          m.StaffId == sid &&
                                          m.PermissionId == f.PermissionId &&
                                          m.HasAccess)
                    }).ToList()
                };
            }).ToList();

            // ── 6. Build grid from extra staff ────────────────────────────────
            var gridFromStaff = extraStaff.Select(s => new
            {
                staffId     = s.StaffId,
                personId    = s.PersonId,
                fullName    = s.Person?.FullName ?? "-",
                loginId     = s.LoginId ?? "-",
                jobTitle    = s.Vacancy?.JobTitle ?? "-",
                isHired     = true,
                permissions = features.Select(f => new
                {
                    featureKey  = f.FeatureKey,
                    featureName = f.FeatureName,
                    module      = f.Module,
                    hasAccess   = matrix.Any(m =>
                                      m.StaffId == s.StaffId &&
                                      m.PermissionId == f.PermissionId &&
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
            // Map FeatureKey → PermissionId
            var featureKeys = items.Select(i => i.FeatureKey).Distinct().ToList();
            var featureMap = await _db.Features
                .Where(f => featureKeys.Contains(f.FeatureKey))
                .ToDictionaryAsync(f => f.FeatureKey, f => f.PermissionId);

            int count = 0;
            var skipped = new List<string>();

            foreach (var item in items)
            {
                if (!featureMap.TryGetValue(item.FeatureKey, out int permId))
                {
                    skipped.Add(item.FeatureKey);
                    continue;
                }

                var existing = await _db.DepartmentAccessMatrix
                    .FirstOrDefaultAsync(m => m.StaffId == item.StaffId && m.PermissionId == permId);

                if (existing == null)
                {
                    _db.DepartmentAccessMatrix.Add(new DepartmentAccessMatrix
                    {
                        StaffId     = item.StaffId,
                        DeptId      = deptId,
                        PermissionId = permId,
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
            if (!await _db.StaffVacancies.AnyAsync(s => s.StaffId == staffId))
                return (false, $"Staff {staffId} not found.");

            // Find or auto-create the feature
            var feature = await _db.Features.FirstOrDefaultAsync(f => f.FeatureKey == featureKey);

            if (feature == null)
            {
                // Auto-create MENU_* features
                if (featureKey.StartsWith("MENU_", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = featureKey.Split('_');
                    int.TryParse(parts.Length >= 2 ? parts[1] : "0", out int menuId);
                    var menu = menuId > 0 ? await _db.Menus.AsNoTracking().FirstOrDefaultAsync(m => m.Id == menuId) : null;
                    string t = menu?.Title ?? $"Menu {menuId}";
                    string suf = parts.Length >= 3 ? string.Join("_", parts.Skip(2)) : "";
                    string name = suf switch
                    {
                        "VIEW"   => $"{t} - View",
                        "ADD"    => $"{t} - Add",
                        "EDIT"   => $"{t} - Edit",
                        "DELETE" => $"{t} - Delete",
                        ""       => t,
                        _        => $"{t} - {suf}"
                    };
                    feature = new Feature { FeatureKey = featureKey, FeatureName = name, Module = "Menu" };
                    _db.Features.Add(feature);
                    try { await _db.SaveChangesAsync(); }
                    catch { /* ignore duplicate key race */ }
                }
                else
                {
                    return (false, $"Feature '{featureKey}' not found. Use GET /api/access/features.");
                }
            }

            // Write to UserPermissionOverrides (primary RBAC table)
            var status = hasAccess ? PermissionStatus.ALLOW : PermissionStatus.DENY;

            var upo = await _db.UserPermissionOverrides
                .FirstOrDefaultAsync(u => u.StaffId == staffId && u.PermissionId == feature.PermissionId);

            if (upo == null)
            {
                _db.UserPermissionOverrides.Add(new UserPermissionOverride
                {
                    StaffId      = staffId,
                    PermissionId = feature.PermissionId,
                    Status       = status.ToString(),
                    SetBy        = grantedBy,
                    SetDate      = DateTime.Now,
                    Reason       = "Set via Access Manager"
                });
            }
            else
            {
                upo.Status  = status.ToString();
                upo.SetBy   = grantedBy;
                upo.SetDate = DateTime.Now;
            }

            // Also sync to DepartmentAccessMatrix (legacy read path)
            var staff = await _db.StaffVacancies.Include(s => s.Vacancy)
                .FirstOrDefaultAsync(s => s.StaffId == staffId);
            int deptId = staff?.Vacancy?.OrganizationId ?? 0;

            if (deptId > 0)
            {
                var matrix = await _db.DepartmentAccessMatrix
                    .FirstOrDefaultAsync(m => m.StaffId == staffId && m.PermissionId == feature.PermissionId);

                if (matrix == null)
                {
                    _db.DepartmentAccessMatrix.Add(new DepartmentAccessMatrix
                    {
                        StaffId      = staffId,
                        DeptId       = deptId,
                        PermissionId = feature.PermissionId,
                        HasAccess    = hasAccess,
                        GrantedBy    = grantedBy,
                        GrantedDate  = DateTime.Now
                    });
                }
                else
                {
                    matrix.HasAccess   = hasAccess;
                    matrix.GrantedBy   = grantedBy;
                    matrix.GrantedDate = DateTime.Now;
                }
            }

            await _db.SaveChangesAsync();
            return (true, $"Permission '{featureKey}' {(hasAccess ? "granted" : "revoked")} for staff {staffId}.");
        }

        public async Task<(int Count, string Message)> GrantAllAsync(
            Guid staffId, int deptId, string? grantedBy)
        {
            // Get staff's dept if deptId not provided
            if (deptId <= 0)
            {
                var s = await _db.StaffVacancies.Include(x => x.Vacancy)
                    .FirstOrDefaultAsync(x => x.StaffId == staffId);
                deptId = s?.Vacancy?.OrganizationId ?? 0;
            }

            var features = await _db.Features.ToListAsync();
            var items = features.Select(f => new MatrixUpdateItem
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
                .AsNoTracking()
                .Include(m => m.Feature)
                .Where(m => m.StaffId == staffId && m.HasAccess)
                .Select(m => m.Feature!.FeatureKey)
                .ToListAsync();

            var groupPerms = await _db.StaffAccessGroups
                .AsNoTracking()
                .Where(s => s.StaffId == staffId)
                .SelectMany(s => s.Group!.Features.Select(f => f.Feature!.FeatureKey))
                .ToListAsync();

            return matrixPerms.Union(groupPerms).Distinct().OrderBy(k => k);
        }

        public async Task<IEnumerable<object>> GetDepartmentPersonsAsync(int deptId)
        {
            // All persons hired into vacancies in this department
            var persons = await _db.Persons
                .Include(p => p.Staff).ThenInclude(s => s!.Vacancy)
                .Where(p => p.Staff != null && p.Staff.Vacancy != null && p.Staff.Vacancy.OrganizationId == deptId)
                .OrderBy(p => p.FullName)
                .ToListAsync();

            return persons.Select(p => (object)new
            {
                personId   = p.PersonId,
                staffId    = p.Staff?.StaffId,
                fullName   = p.FullName,
                loginId    = p.Staff?.LoginId,
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
            var staff = await _db.StaffVacancies
                .AsNoTracking()
                .Include(s => s.Person)
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
            var individualAccess = await _db.DepartmentAccessMatrix
                .AsNoTracking()
                .Where(m => m.StaffId == staffId && m.HasAccess)
                .Select(m => m.PermissionId)
                .ToHashSetAsync();

            // ── 5. Load group feature IDs ──────────────────────────────────────
            var groupAccess = await _db.AccessGroupFeatures
                .AsNoTracking()
                .Where(f => f.GroupId == groupId)
                .Select(f => f.PermissionId)
                .ToHashSetAsync();

            // ── 6. Merge — feature is accessible if EITHER source grants it ───
            var mergedFeatures = allFeatures.Select(f => new EffectiveFeatureAccess
            {
                FeatureKey       = f.FeatureKey,
                FeatureName      = f.FeatureName,
                Module           = f.Module,
                IndividualAccess = individualAccess.Contains(f.PermissionId),
                GroupAccess      = groupAccess.Contains(f.PermissionId)
            }).ToList();

            return new EffectiveAccessResult
            {
                StaffId   = staffId,
                GroupId   = groupId,
                StaffName = staff.Person?.FullName ?? "-",
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
                .Include(g => g.Features).ThenInclude(f => f.Feature)
                .FirstOrDefaultAsync(g => g.GroupId == groupId);

            if (group == null)
                return (false, $"Group {groupId} not found.", 0, 0);

            // ── 2. Load group's current permission IDs ────────────────────────
            var groupPermissionIds = group.Features.Select(f => f.PermissionId).ToHashSet();

            // ── 3. Load all staff who belong to this group ────────────────────
            var groupMembers = await _db.StaffAccessGroups
                .AsNoTracking()
                .Include(s => s.Staff).ThenInclude(s => s!.Vacancy)
                .Where(s => s.GroupId == groupId)
                .Select(s => new
                {
                    s.StaffId,
                    FullName = s.Staff!.Person != null ? s.Staff.Person.FullName : "-",
                    DeptId = s.Staff.Vacancy != null ? s.Staff.Vacancy.OrganizationId : 0
                })
                .ToListAsync();

            if (!groupMembers.Any())
                return (true, $"Group '{group.GroupName}' has no members. Nothing to sync.", 0, 0);

            int staffSynced       = 0;
            int permissionsSynced = 0;

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
                            int deptId = member.DeptId;

                            var existingRows = await _db.DepartmentAccessMatrix
                                .Where(m => m.StaffId == member.StaffId)
                                .ToListAsync();

                            var existingByPermId = existingRows.ToDictionary(r => r.PermissionId);

                            // Grant: features the group now has
                            foreach (var permId in groupPermissionIds)
                            {
                                if (existingByPermId.TryGetValue(permId, out var row))
                                {
                                    if (!row.HasAccess)
                                    {
                                        row.HasAccess   = true;
                                        row.GrantedBy   = $"GroupSync:{group.GroupName}";
                                        row.GrantedDate = DateTime.Now;
                                        permissionsSynced++;
                                    }
                                }
                                else
                                {
                                    _db.DepartmentAccessMatrix.Add(new DepartmentAccessMatrix
                                    {
                                        StaffId      = member.StaffId,
                                        DeptId       = deptId,
                                        PermissionId = permId,
                                        HasAccess    = true,
                                        GrantedBy    = $"GroupSync:{group.GroupName}",
                                        GrantedDate  = DateTime.Now
                                    });
                                    permissionsSynced++;
                                }
                            }

                            // Revoke: features the group no longer has (only if previously set by group sync)
                            foreach (var row in existingRows)
                            {
                                if (!groupPermissionIds.Contains(row.PermissionId) && row.HasAccess)
                                {
                                    bool wasGroupGranted = row.GrantedBy != null &&
                                        row.GrantedBy.StartsWith("GroupSync:");

                                    if (wasGroupGranted)
                                    {
                                        row.HasAccess   = false;
                                        row.GrantedBy   = $"GroupSync:{group.GroupName}";
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
                        throw;
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
                return (false, $"Sync failed and was rolled back. Error: {ex.Message}", 0, 0);
            }
        }
    }
}
