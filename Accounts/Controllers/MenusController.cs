using Accounts.DTOs;
using Accounts.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Accounts.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MenusController : ControllerBase
    {
        private readonly IMenuService _menuService;

        public MenusController(IMenuService menuService)
        {
            _menuService = menuService;
        }

        // POST api/menus — create a new menu item
        [HttpPost]
        public async Task<IActionResult> CreateMenu([FromBody] CreateMenuDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var menu = await _menuService.CreateMenuAsync(dto);
            return CreatedAtAction(nameof(GetSidebarTree), new { }, menu);
        }

        // GET api/menus/sidebar-tree — dynamic tree for the sidebar (RBAC-aware)
        [HttpGet("sidebar-tree")]
        public async Task<IActionResult> GetSidebarTree()
        {
            // Extract roles from the JWT/cookie if the user is authenticated
            var userRoles = User.Claims
                .Where(c => c.Type == ClaimTypes.Role)
                .Select(c => c.Value)
                .ToList();

            var tree = await _menuService.GetSidebarTreeAsync(userRoles.Count > 0 ? userRoles : null);
            return Ok(tree);
        }

        // GET api/menus — flat list for admin management
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var menus = await _menuService.GetAllAsync();
            return Ok(menus);
        }

        // DELETE api/menus/{id} — soft-delete (deactivate)
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Deactivate(int id)
        {
            var success = await _menuService.DeactivateAsync(id);
            if (!success) return NotFound(new { message = $"Menu {id} not found." });
            return NoContent();
        }
    }
}
