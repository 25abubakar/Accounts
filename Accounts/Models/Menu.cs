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
        public List<MenuRole> MenuRoles { get; set; } = new();
    }

    [Table("MenuRoles")]
    public class MenuRole
    {
        public int MenuId { get; set; }

        [Required, MaxLength(50)]
        public string RoleName { get; set; } = string.Empty;

        public Menu? Menu { get; set; }
    }
}
