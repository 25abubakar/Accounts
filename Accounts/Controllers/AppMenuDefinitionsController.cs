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
                    MenuCode   = m.Id.ToString(),
                    MenuName   = m.Title,
                    ModuleName = null, 
                    RoutePath  = m.Route, 
                    IconCss    = m.Icon,
                    SortOrder  = m.SortOrder
                })
                .ToListAsync(ct);

            return Ok(CommApiResponse<List<AppMenuDefinitionDto>>.Ok(menus));
        }
    }
}
