namespace Accounts.Services.Interfaces
{
    /// <summary>
    /// Feature/permission management service.
    /// Access Groups and DepartmentAccessMatrix are deprecated.
    /// All permission writes go through UserPermissionOverrides (via RbacService).
    /// </summary>
    public interface IAccessService
    {
        Task<IEnumerable<object>> GetAllFeaturesAsync();
        Task<IEnumerable<object>> GetFeaturesByModuleAsync(string module);

        Task<IEnumerable<string>> GetStaffPermissionsAsync(Guid staffId);

        Task<(bool Success, string Message)> TogglePermissionAsync(
            Guid staffId, string featureKey, bool hasAccess, string? grantedBy);

        Task<(int Count, string Message)> GrantAllAsync(Guid staffId, int deptId, string? grantedBy);
        Task<(int Count, string Message)> RevokeAllAsync(Guid staffId, string? grantedBy);

        Task<IEnumerable<object>> GetDepartmentPersonsAsync(int deptId);
    }
}
