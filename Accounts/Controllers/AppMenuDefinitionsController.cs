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
        // Reads from the Menus table (AppMenuDefinitions was dropped in V2 migration).
        // Maps: Menu.Id → MenuCode (as string), Menu.Title → MenuName,
        //       Menu.Route → RoutePath, Menu.Icon → IconCss.
        [HttpGet("active")]
        public async Task<IActionResult> GetActive(CancellationToken ct)
        {
            var menus = await _db.Menus
                .AsNoTracking()
                .Where(m => m.IsActive)
                .OrderBy(m => m.SortOrder)
                .ThenBy(m => m.Title)
                .Select(m => new AppMenuDefinitionDto
                {
                    MenuCode   = m.Id.ToString(),   // Menu.Id  → MenuCode
                    MenuName   = m.Title,            // Menu.Title → MenuName
                    ModuleName = null,               // Menus table has no ModuleName
                    RoutePath  = m.Route,            // Menu.Route → RoutePath
                    IconCss    = m.Icon,             // Menu.Icon  → IconCss
                    SortOrder  = m.SortOrder
                })
                .ToListAsync(ct);

            return Ok(CommApiResponse<List<AppMenuDefinitionDto>>.Ok(menus));
        }
    }
}
