using Accounts.Data;
using Accounts.DTOs.CommCenter;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Controllers
{
    [ApiController]
    [Route("api/app-lookups")]
    public class AppLookupsController : ControllerBase
    {
        private readonly ApplicationDbContext _db;

        public AppLookupsController(ApplicationDbContext db) => _db = db;

        // GET /api/app-lookups/all
        [HttpGet("all")]
        public async Task<IActionResult> GetAll(CancellationToken ct)
        {
            var values = await _db.AppLookupValues
                .AsNoTracking()
                .Include(v => v.LookupType)
                .Where(v => v.IsActive && v.LookupType != null && v.LookupType.IsActive)
                .OrderBy(v => v.LookupType!.LookupTypeCode)
                .ThenBy(v => v.SortOrder)
                .Select(v => new AppLookupDto
                {
                    LookupTypeCode = v.LookupType!.LookupTypeCode,
                    ValueCode      = v.ValueCode,
                    DisplayText    = v.DisplayText,
                    SortOrder      = v.SortOrder,
                    IsDefault      = v.IsDefault,
                    MetadataJson   = v.MetadataJson
                })
                .ToListAsync(ct);

            return Ok(CommApiResponse<List<AppLookupDto>>.Ok(values));
        }

        // GET /api/app-lookups/{lookupTypeCode}
        [HttpGet("{lookupTypeCode}")]
        public async Task<IActionResult> GetByType(string lookupTypeCode, CancellationToken ct)
        {
            var values = await _db.AppLookupValues
                .AsNoTracking()
                .Include(v => v.LookupType)
                .Where(v => v.IsActive
                         && v.LookupType != null
                         && v.LookupType.IsActive
                         && v.LookupType.LookupTypeCode == lookupTypeCode)
                .OrderBy(v => v.SortOrder)
                .Select(v => new AppLookupDto
                {
                    LookupTypeCode = v.LookupType!.LookupTypeCode,
                    ValueCode      = v.ValueCode,
                    DisplayText    = v.DisplayText,
                    SortOrder      = v.SortOrder,
                    IsDefault      = v.IsDefault,
                    MetadataJson   = v.MetadataJson
                })
                .ToListAsync(ct);

            return Ok(CommApiResponse<List<AppLookupDto>>.Ok(values));
        }
    }
}
