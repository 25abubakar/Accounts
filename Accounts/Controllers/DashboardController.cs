using Accounts.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Controllers;

[ApiController]
[Route("api/dashboard")]
[Authorize]
[Produces("application/json")]
public sealed class DashboardController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public DashboardController(ApplicationDbContext db) => _db = db;

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary(CancellationToken cancellationToken)
    {
        if (User.IsInRole("SuperAdmin"))
        {
            return Ok(new
            {
                staffCount = 0,
                totalSeats = 0,
                filledSeats = 0,
                vacantSeats = 0,
                fillRate = 0,
                topCountries = Array.Empty<object>(),
                topRoles = Array.Empty<object>()
            });
        }

        var seats = await _db.Vacancies.AsNoTracking()
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Total = group.Count(),
                Filled = group.Count(vacancy => vacancy.IsFilled)
            })
            .FirstOrDefaultAsync(cancellationToken);

        var totalSeats = seats?.Total ?? 0;
        var filledSeats = seats?.Filled ?? 0;
        var staffCount = await _db.StaffVacancies.AsNoTracking()
            .CountAsync(cancellationToken);

        var topRoles = await _db.Vacancies.AsNoTracking()
            .GroupBy(vacancy => vacancy.JobTitleNav != null
                ? vacancy.JobTitleNav.TitleName
                : vacancy.JobTitle)
            .Select(group => new { name = group.Key ?? "Unassigned", count = group.Count() })
            .OrderByDescending(item => item.count)
            .ThenBy(item => item.name)
            .Take(6)
            .ToListAsync(cancellationToken);

        var topCountries = await _db.Vacancies.AsNoTracking()
            .Where(vacancy => vacancy.IsFilled)
            .GroupBy(vacancy => vacancy.Organization != null &&
                                vacancy.Organization.Parent != null &&
                                vacancy.Organization.Parent.Parent != null
                ? vacancy.Organization.Parent.Parent.Name
                : "Unknown")
            .Select(group => new { name = group.Key, count = group.Count() })
            .OrderByDescending(item => item.count)
            .ThenBy(item => item.name)
            .Take(5)
            .ToListAsync(cancellationToken);

        var vacantSeats = totalSeats - filledSeats;
        return Ok(new
        {
            staffCount,
            totalSeats,
            filledSeats,
            vacantSeats,
            fillRate = totalSeats == 0
                ? 0
                : (int)Math.Round(filledSeats * 100d / totalSeats),
            topCountries,
            topRoles
        });
    }
}
