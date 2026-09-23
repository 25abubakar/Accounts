using Accounts.Data;
using Accounts.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Controllers;

[ApiController]
[Authorize]
[Produces("application/json")]
[Route("api")]
public sealed class AccountsLookupsController(ApplicationDbContext db) : ControllerBase
{
    [HttpGet("transaction-modes")]
    public async Task<IActionResult> Modes(CancellationToken ct) => Ok(ApiResponse<object>.Ok(await db.AccountsTransModes.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).Select(x => new { x.Id, x.Code, x.Name }).ToListAsync(ct)));

    [HttpGet("transaction-types")]
    public async Task<IActionResult> Types(CancellationToken ct) => Ok(ApiResponse<object>.Ok(await db.AccountsTransTypes.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).Select(x => new { x.Id, x.Code, x.Name }).ToListAsync(ct)));

    [HttpGet("tax-types")]
    public async Task<IActionResult> Taxes(CancellationToken ct) => Ok(ApiResponse<object>.Ok(await db.AccountsTaxTypes.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).Select(x => new { x.Id, x.Code, x.Name, x.DefaultRate }).ToListAsync(ct)));

    [HttpGet("statuses/account-entry")]
    public async Task<IActionResult> Statuses(CancellationToken ct) => Ok(ApiResponse<object>.Ok(await db.AccountsEntryStatuses.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).Select(x => new { x.Id, x.Code, x.Name, x.ColorCode, x.FontColor }).ToListAsync(ct)));
}
