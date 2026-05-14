namespace Accounts.DTOs
{
    public class CreateMenuDto
    {
        public string Title { get; set; } = string.Empty;
        public string? Icon { get; set; }
        public string? Route { get; set; }
        public int? ParentId { get; set; }
        public int SortOrder { get; set; }

        /// <summary>Role names that can see this menu item, e.g. ["Admin", "HR"]. Empty = visible to all.</summary>
        public List<string> RequiredRoles { get; set; } = new();
    }

    public class MenuTreeNodeDto
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Icon { get; set; }
        public string? Route { get; set; }
        public int SortOrder { get; set; }
        public List<string> Roles { get; set; } = new();
        public List<MenuTreeNodeDto> Children { get; set; } = new();
    }
}
