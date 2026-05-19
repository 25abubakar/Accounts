using Accounts.Models;

namespace Accounts.Services.Interfaces
{
    public interface IAccessService
    {
        Task<IEnumerable<object>> GetAllFeaturesAsync();
        Task<IEnumerable<object>> GetFeaturesByModuleAsync(string module);
        Task<IEnumerable<object>> GetAllGroupsAsync();
        Task<object?> GetGroupByIdAsync(int groupId);
        Task<AccessGroup> CreateGroupAsync(string groupName, string? description);
        Task<bool> UpdateGroupAsync(int groupId, string groupName, string? description);
        Task<bool> DeleteGroupAsync(int groupId);

        Task<(bool Success, string Message)> SetGroupFeaturesAsync(int groupId, IEnumerable<string> featureKeys);

        Task<(bool Success, string Message)> AssignGroupToStaffAsync(Guid staffId, int groupId, string? assignedBy, string? note);
        Task<(bool Success, string Message)> RemoveGroupFromStaffAsync(Guid staffId, int groupId);
        Task<IEnumerable<object>> GetStaffGroupsAsync(Guid staffId);

        Task<object> GetDepartmentMatrixAsync(int deptId);

        Task<(int Updated, string Message)> SaveDepartmentMatrixAsync(
            int deptId, IEnumerable<MatrixUpdateItem> items, string? grantedBy);

        Task<(bool Success, string Message)> TogglePermissionAsync(
            Guid staffId, string featureKey, bool hasAccess, string? grantedBy);

        Task<(int Count, string Message)> GrantAllAsync(Guid staffId, int deptId, string? grantedBy);

        Task<(int Count, string Message)> RevokeAllAsync(Guid staffId, string? grantedBy);

        Task<IEnumerable<string>> GetStaffPermissionsAsync(Guid staffId);

        /// <summary>Get all persons in a department (BranchId match) — hired and not hired</summary>
        Task<IEnumerable<object>> GetDepartmentPersonsAsync(int deptId);

        // ── New: Effective Access + Group Sync ────────────────────────────────

        /// <summary>
        /// Returns the merged effective access for a staff member.
        /// Priority: Individual DepartmentAccessMatrix OR Group AccessGroupFeatures.
        /// A feature is accessible if EITHER the individual matrix OR any assigned group grants it.
        /// </summary>
        Task<EffectiveAccessResult> GetEffectiveAccessAsync(Guid staffId, int groupId);

        /// <summary>
        /// When a group's features are updated, sync those changes into DepartmentAccessMatrix
        /// for every staff member who belongs to that group.
        /// Runs inside a transaction — rolls back fully on any failure.
        /// </summary>
        Task<(bool Success, string Message, int StaffSynced, int PermissionsSynced)> SyncGroupToDeptMatrixAsync(
            int groupId, string? syncedBy = null);
    }

    public class MatrixUpdateItem
    {
        public Guid   StaffId    { get; set; }
        public string FeatureKey { get; set; } = string.Empty;
        public bool   HasAccess  { get; set; }
    }

    /// <summary>Result of GetEffectiveAccessAsync — merged view of group + individual access.</summary>
    public class EffectiveAccessResult
    {
        public Guid   StaffId  { get; set; }
        public int    GroupId  { get; set; }
        public string StaffName { get; set; } = string.Empty;
        public string GroupName { get; set; } = string.Empty;

        /// <summary>
        /// Each feature with its effective access and the source that granted it.
        /// Source: "Individual" | "Group" | "Both" | "None"
        /// </summary>
        public List<EffectiveFeatureAccess> Features { get; set; } = new();

        public int TotalGranted => Features.Count(f => f.HasAccess);
        public int TotalDenied  => Features.Count(f => !f.HasAccess);
    }

    public class EffectiveFeatureAccess
    {
        public string FeatureKey  { get; set; } = string.Empty;
        public string FeatureName { get; set; } = string.Empty;
        public string Module      { get; set; } = string.Empty;

        /// <summary>True if individual matrix grants it</summary>
        public bool IndividualAccess { get; set; }

        /// <summary>True if the group grants it</summary>
        public bool GroupAccess { get; set; }

        /// <summary>Final merged result — true if EITHER individual OR group grants it</summary>
        public bool HasAccess => IndividualAccess || GroupAccess;

        /// <summary>Where the access came from: Individual / Group / Both / None</summary>
        public string Source => (IndividualAccess, GroupAccess) switch
        {
            (true,  true)  => "Both",
            (true,  false) => "Individual",
            (false, true)  => "Group",
            _              => "None"
        };
    }
}
