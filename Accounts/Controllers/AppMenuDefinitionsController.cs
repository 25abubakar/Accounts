using Accounts.Data;
using Accounts.DTOs.CommCenter;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Controllers
{
    [ApiController]
    [Route("api/app-menu-definitions")]
    public class AppMenuDefinitionsController : ControllerBase
    {
        private readonly ApplicationDbContext _db;

        public AppMenuDefinitionsController(ApplicationDbContext db) => _db = db;

        // GET /api/app-menu-definitions/active
        [HttpGet("active")]
        public async Task<IActionResult> GetActive(CancellationToken ct)
        {
            var menus = await _db.AppMenuDefinitions
                .AsNoTracking()
                .Where(m => m.IsActive)
                .OrderBy(m => m.SortOrder)
                .ThenBy(m => m.MenuName)
                .Select(m => new AppMenuDefinitionDto
                {
                    MenuCode   = m.MenuCode,
                    MenuName   = m.MenuName,
                    ModuleName = m.ModuleName,
                    RoutePath  = m.RoutePath,
                    IconCss    = m.IconCss,
                    SortOrder  = m.SortOrder
                })
                .ToListAsync(ct);

            return Ok(CommApiResponse<List<AppMenuDefinitionDto>>.Ok(menus));
        }
    }
}
