namespace Accounts.DTOs
{
    /// <summary>
    /// Optimized menu response DTOs for frontend consumption.
    /// Generated from a single optimized query with in-memory filtering.
    /// </summary>
    public class MenuResponseDto
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Icon { get; set; }
        public string? Route { get; set; }
        public int SortOrder { get; set; }
        public List<MenuResponseDto> Children { get; set; } = new();
    }

    /// <summary>
    /// Permission info for frontend authorization (optional, for debugging).
    /// </summary>
    public class PermissionDto
    {
        public int PermissionId { get; set; }
        public string FeatureKey { get; set; } = string.Empty;
        public string FeatureName { get; set; } = string.Empty;
        public string Module { get; set; } = string.Empty;
        public bool HasAccess { get; set; }
        public string Source { get; set; } = string.Empty; // "UserAllow", "UserDeny", "RoleDefault", "Matrix", "AccessGroup", "Denied"
    }

    /// <summary>
    /// Complete user session payload including sidebar and permissions.
    /// </summary>
    public class UserMenuSessionDto
    {
        public Guid? StaffId { get; set; }
        public bool IsFullAccess { get; set; }
        public List<MenuResponseDto> Sidebar { get; set; } = new();
        public List<int> AllowedPermissionIds { get; set; } = new(); // For frontend caching
        public List<PermissionDto>? DetailedPermissions { get; set; } // Optional detailed view
    }
}
