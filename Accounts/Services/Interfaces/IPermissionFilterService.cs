namespace Accounts.Services.Interfaces
{
    /// <summary>
    /// Service for filtering data based on user's effective permissions.
    /// Only returns data the user has access to view.
    /// </summary>
    public interface IPermissionFilterService
    {
        /// <summary>
        /// Get all data the current user has permission to access.
        /// Returns filtered lists of departments, staff, persons, etc.
        /// </summary>
        Task<object> GetAccessibleDataAsync(Guid staffId);

        /// <summary>
        /// Check if user has access to a specific feature.
        /// </summary>
        Task<bool> CanAccessFeatureAsync(Guid staffId, string featureKey);

        /// <summary>
        /// Get all features the user has access to.
        /// </summary>
        Task<IEnumerable<string>> GetAccessibleFeaturesAsync(Guid staffId);

        /// <summary>
        /// Get departments the user can view based on their permissions.
        /// </summary>
        Task<IEnumerable<object>> GetAccessibleDepartmentsAsync(Guid staffId);

        /// <summary>
        /// Get staff members the user can view based on their permissions.
        /// </summary>
        Task<IEnumerable<object>> GetAccessibleStaffAsync(Guid staffId);

        /// <summary>
        /// Get persons the user can view based on their permissions.
        /// </summary>
        Task<IEnumerable<object>> GetAccessiblePersonsAsync(Guid staffId);
    }
}
