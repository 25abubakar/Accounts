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
    }

    public class MatrixUpdateItem
    {
        public Guid   StaffId    { get; set; }
        public string FeatureKey { get; set; } = string.Empty;
        public bool   HasAccess  { get; set; }
    }
}
