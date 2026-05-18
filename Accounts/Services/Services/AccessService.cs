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

            var existing = await _db.AccessGroupFeatures
                .Where(x => x.GroupId == groupId).ToListAsync();
            _db.AccessGroupFeatures.RemoveRange(existing);

            foreach (var key in featureKeys.Distinct())
            {
                if (await _db.Features.AnyAsync(f => f.FeatureKey == key))
                    _db.AccessGroupFeatures.Add(new AccessGroupFeature { GroupId = groupId, FeatureKey = key });
            }

            await _db.SaveChangesAsync();
            return (true, "Features updated.");
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
            int count = 0;
            foreach (var item in items)
            {
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
            await _db.SaveChangesAsync();
            return (count, $"{count} permissions updated.");
        }

        public async Task<(bool Success, string Message)> TogglePermissionAsync(
            Guid staffId, string featureKey, bool hasAccess, string? grantedBy)
        {
            if (!await _db.Staff.AnyAsync(s => s.StaffId == staffId))
                return (false, $"Staff {staffId} not found.");
            if (!await _db.Features.AnyAsync(f => f.FeatureKey == featureKey))
                return (false, $"Feature '{featureKey}' not found.");

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
                existing.GrantedDate = DateTime.UtcNow;
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
    }
}
