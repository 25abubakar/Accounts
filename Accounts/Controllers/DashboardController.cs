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
                trend = Array.Empty<object>(),
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

        var today = GetPakistanToday();
        var trendStart = today.AddDays(-6);
        var trendDates = Enumerable.Range(0, 7)
            .Select(offset => trendStart.AddDays(offset))
            .ToArray();

        var vacancyTrendSource = await _db.Vacancies.AsNoTracking()
            .Where(vacancy => vacancy.CreatedDate.Date <= today)
            .Select(vacancy => new
            {
                CreatedDate = vacancy.CreatedDate.Date,
                vacancy.IsFilled,
                FilledDate = vacancy.Staff != null && vacancy.Staff.Person != null
                    ? (DateTime?)vacancy.Staff.Person.CreatedDate.Date
                    : null
            })
            .ToListAsync(cancellationToken);

        var trend = trendDates.Select(date =>
        {
            var totalForDate = vacancyTrendSource.Count(vacancy => vacancy.CreatedDate <= date);
            var filledForDate = vacancyTrendSource.Count(vacancy =>
                vacancy.IsFilled &&
                (vacancy.FilledDate == null || vacancy.FilledDate.Value.Date <= date));

            return new
            {
                date = date.ToString("yyyy-MM-dd"),
                label = date.ToString("dd MMM"),
                totalSeats = totalForDate,
                filledSeats = filledForDate,
                vacantSeats = Math.Max(0, totalForDate - filledForDate)
            };
        }).ToArray();

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
            trend,
            topCountries,
            topRoles
        });
    }

    private static DateTime GetPakistanToday()
    {
        var zone = FindPakistanTimeZone();
        return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone).Date;
    }

    private static TimeZoneInfo FindPakistanTimeZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Pakistan Standard Time"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Karachi"); }
        catch (InvalidTimeZoneException) { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Karachi"); }
    }
}
