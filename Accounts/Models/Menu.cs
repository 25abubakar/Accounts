using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models
{
    [Table("Menus")]
    public class Menu
    {
        public int Id { get; set; }

        [Required, MaxLength(100)]
        public string Title { get; set; } = string.Empty;

        [MaxLength(50)]
        public string? Icon { get; set; }

        [MaxLength(200)]
        public string? Route { get; set; }

        public int? ParentId { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;

        // Navigation properties
        public Menu? Parent { get; set; }
        public List<Menu> Children { get; set; } = new();

        /// <summary>
        /// Required permissions to see this menu item.
        /// Each MenuPermission links to Features via PermissionId (int FK).
        /// An empty list means the item is public (all authenticated users can see it).
        /// </summary>
        public List<MenuPermission> MenuPermissions { get; set; } = new();
    }

    /// <summary>
    /// Maps a Menu to the Features (permissions) required to see it.
    /// Uses integer PermissionId FK — no more raw strings.
    /// If you rename a feature, all menus stay linked because the FK is stable.
    /// </summary>
    [Table("MenuPermissions")]
    public class MenuPermission
    {
        public int MenuId       { get; set; }   // FK → Menus.Id
        public int PermissionId { get; set; }   // FK → Features.PermissionId

        [ForeignKey("MenuId")]
        public Menu? Menu { get; set; }

        [ForeignKey("PermissionId")]
        public Feature? Feature { get; set; }
    }
}
