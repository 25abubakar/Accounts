using Accounts.DTOs;
using Accounts.Models;

namespace Accounts.Services.Interfaces
{
    public interface IMenuService
    {
        Task<Menu> CreateMenuAsync(CreateMenuDto dto);

        Task<Menu?> UpdateMenuAsync(int id, CreateMenuDto dto);

        Task<List<MenuTreeNodeDto>> GetSidebarTreeAsync(IEnumerable<string>? userRoles = null);

        Task<List<Menu>> GetAllAsync();

        Task<bool> DeactivateAsync(int id);
    }
}
