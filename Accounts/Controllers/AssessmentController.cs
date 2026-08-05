using Accounts.Data;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Accounts.Services.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Accounts.Controllers;

[ApiController]
[Route("api/assessment")]
[Authorize]
[Produces("application/json")]
public sealed class AssessmentController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly RbacService _rbac;
    private readonly IOrganizationDataScopeService _dataScope;
    private readonly ITenantService _tenant;

    public AssessmentController(ApplicationDbContext db, RbacService rbac, IOrganizationDataScopeService dataScope, ITenantService tenant)
    {
        _db = db;
        _rbac = rbac;
        _dataScope = dataScope;
        _tenant = tenant;
    }

    [HttpGet("staff-hierarchy")]
    public async Task<IActionResult> GetStaffHierarchy([FromQuery] int? year, [FromQuery] int? month, CancellationToken ct)
    {
        var identityUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(identityUserId)) return Unauthorized();

        var isSuperAdmin = User.IsInRole("SuperAdmin") || string.Equals(
            User.FindFirstValue(ITenantService.ClaimIsSuperAdmin), "true", StringComparison.OrdinalIgnoreCase);
        var isTenantAdmin = User.IsInRole("TenantAdmin") || string.Equals(
            User.FindFirstValue(ITenantService.ClaimIsTenantAdmin), "true", StringComparison.OrdinalIgnoreCase);

        var current = await _db.Persons.AsNoTracking()
            .Where(person => person.IdentityUserId == identityUserId && person.Staff != null)
            .Select(person => new
            {
                person.PersonId,
                person.TenantId,
                StaffId = person.Staff!.StaffId,
                OrganizationId = person.Staff.Vacancy != null
                    ? (int?)person.Staff.Vacancy.OrganizationId
                    : null,
                JobTitle = person.Staff.Vacancy != null
                    ? (person.Staff.Vacancy.JobTitleNav != null
                        ? person.Staff.Vacancy.JobTitleNav.TitleName
                        : person.Staff.Vacancy.JobTitle)
                    : null,
                AttendanceScope = person.Staff.Vacancy != null && person.Staff.Vacancy.JobTitleNav != null
                    ? person.Staff.Vacancy.JobTitleNav.AttendanceVisibilityScope
                    : AttendanceVisibilityScope.Self
            })
            .FirstOrDefaultAsync(ct);

        if (!isSuperAdmin && !isTenantAdmin)
        {
            if (current == null) return Forbid();
            var menuId = await _db.Menus.AsNoTracking()
                .Where(menu => menu.IsActive && menu.Route == "/assessment/mark")
                .Select(menu => (int?)menu.Id)
                .FirstOrDefaultAsync(ct);
            if (!menuId.HasValue ||
                (!await _rbac.HasAccessAsync(current.StaffId, $"MENU_{menuId.Value}") &&
                 !await _rbac.HasAccessAsync(current.StaffId, $"MENU_{menuId.Value}_VIEW")))
                return Forbid();
        }

        var people = await _db.Persons.AsNoTracking()
            .Where(person => person.IsActive && person.Staff != null)
            .Select(person => new HierarchyStaffRow
            {
                PersonId = person.PersonId,
                TenantId = person.TenantId,
                OrganizationId = person.Staff!.Vacancy != null
                    ? (int?)person.Staff.Vacancy.OrganizationId
                    : null,
                StaffGuid = person.Staff!.StaffId,
                StaffId = person.Staff.LoginId ?? person.Staff.StaffId.ToString(),
                FullName = person.FullName,
                Department = person.Staff.Vacancy != null
                    ? (person.Staff.Vacancy.Organization != null && person.Staff.Vacancy.Organization.Label == "Department"
                        ? person.Staff.Vacancy.Organization.Name
                        : person.Staff.Vacancy.Department)
                    : null,
                JobTitle = person.Staff.Vacancy != null
                    ? (person.Staff.Vacancy.JobTitleNav != null
                        ? person.Staff.Vacancy.JobTitleNav.TitleName
                        : person.Staff.Vacancy.JobTitle)
                    : null
            })
            .ToListAsync(ct);

        if (isSuperAdmin) return Ok(Array.Empty<object>());
        if (!isTenantAdmin && current == null) return Ok(Array.Empty<object>());
        var directSubjectIds = isTenantAdmin ? people.Select(person => person.PersonId).ToHashSet()
            : await ResolveDirectSubjectIdsAsync(identityUserId, current!.PersonId, current.JobTitle, ct);
        var visible = people.Where(person => directSubjectIds.Contains(person.PersonId))
            .OrderBy(person => person.Department).ThenBy(person => person.FullName).ToList();
        var assessmentYear = year is >= 2000 and <= 2100 ? year.Value : DateTime.Today.Year;
        var assessmentMonth = month is >= 1 and <= 12 ? month.Value : DateTime.Today.Month;
        var tenantId = current?.TenantId ?? _tenant.TenantId;
        if (!tenantId.HasValue) return Ok(Array.Empty<object>());
        var saved = await _db.StaffAssessments.AsNoTracking()
            .Where(item => item.TenantId == tenantId.Value && item.AssessmentYear == assessmentYear &&
                           item.AssessmentMonth == assessmentMonth &&
                           (isTenantAdmin || item.AssessorPersonId == current!.PersonId))
            .Select(item => new { item.SubjectPersonId, item.Rating, item.Remarks })
            .ToListAsync(ct);
        var savedByPerson = saved.GroupBy(item => item.SubjectPersonId)
            .ToDictionary(group => group.Key, group => group.First());
        var bonusRules = await _db.AssessmentBonusRules.AsNoTracking().Where(rule => rule.IsActive)
            .OrderBy(rule => rule.RankNumber).ToListAsync(ct);
        decimal? BonusFor(byte? rank)
        {
            if (!rank.HasValue) return null;
            var exact = bonusRules.FirstOrDefault(rule => rule.RankNumber == rank.Value);
            if (exact != null) return exact.BonusAmount;
            return bonusRules.Where(rule => rule.AppliesToHigherRanks && rank.Value > rule.RankNumber)
                .OrderByDescending(rule => rule.RankNumber).Select(rule => (decimal?)rule.BonusAmount).FirstOrDefault();
        }

        return Ok(visible.Select((person, index) => new
        {
            id = index + 1,
            personId = person.PersonId,
            staffGuid = person.StaffGuid,
            person.StaffId,
            person.FullName,
            department = person.Department ?? "—",
            jobTitle = person.JobTitle ?? "—",
            person.HierarchyLevel,
            rating = savedByPerson.GetValueOrDefault(person.PersonId)?.Rating,
            remarks = savedByPerson.GetValueOrDefault(person.PersonId)?.Remarks,
            bonusAmount = BonusFor(savedByPerson.GetValueOrDefault(person.PersonId)?.Rating),
            canEdit = !isTenantAdmin
        }));
    }

    [HttpPut("staff/{subjectPersonId:guid}")]
    public async Task<IActionResult> Save(Guid subjectPersonId, [FromBody] SaveAssessmentDto dto, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Unauthorized();
        if (dto.Year is < 2000 or > 2100 || dto.Month is < 1 or > 12 || dto.Rating is < 1 or > 255)
            return BadRequest(new { message = "Valid month, year and a position from 1 to 255 are required." });
        if (string.IsNullOrWhiteSpace(dto.Remarks)) return BadRequest(new { message = "Monthly progress remarks are required." });
        if (dto.Remarks.Trim().Length > 2000) return BadRequest(new { message = "Remarks cannot exceed 2000 characters." });

        var assessor = await _db.Persons.AsNoTracking().Where(person => person.IdentityUserId == userId && person.IsActive)
            .Select(person => new { person.PersonId, person.TenantId, JobTitle = person.Staff != null && person.Staff.Vacancy != null
                ? (person.Staff.Vacancy.JobTitleNav != null ? person.Staff.Vacancy.JobTitleNav.TitleName : person.Staff.Vacancy.JobTitle) : null })
            .FirstOrDefaultAsync(ct);
        if (assessor == null) return Forbid();
        var allowed = await ResolveDirectSubjectIdsAsync(userId, assessor.PersonId, assessor.JobTitle, ct);
        if (!allowed.Contains(subjectPersonId)) return Forbid();
        var duplicateRank = await _db.StaffAssessments.AsNoTracking().AnyAsync(item =>
            item.TenantId == assessor.TenantId && item.AssessorPersonId == assessor.PersonId &&
            item.SubjectPersonId != subjectPersonId && item.AssessmentYear == dto.Year &&
            item.AssessmentMonth == dto.Month && item.Rating == dto.Rating, ct);
        if (duplicateRank) return Conflict(new { message = $"Position {dto.Rating} is already assigned to another team member for this month." });

        var assessment = await _db.StaffAssessments.SingleOrDefaultAsync(item =>
            item.TenantId == assessor.TenantId && item.AssessorPersonId == assessor.PersonId &&
            item.SubjectPersonId == subjectPersonId && item.AssessmentYear == dto.Year &&
            item.AssessmentMonth == dto.Month, ct);
        if (assessment == null)
        {
            assessment = new StaffAssessment
            {
                TenantId = assessor.TenantId,
                AssessorPersonId = assessor.PersonId,
                SubjectPersonId = subjectPersonId,
                AssessmentYear = dto.Year,
                AssessmentMonth = (byte)dto.Month,
                Rating = (byte)dto.Rating,
                Remarks = dto.Remarks.Trim(),
                CreatedDateUtc = DateTime.UtcNow
            };
            _db.StaffAssessments.Add(assessment);
        }
        else
        {
            assessment.Rating = (byte)dto.Rating;
            assessment.Remarks = dto.Remarks.Trim();
            assessment.ModifiedDateUtc = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync(ct);
        return Ok(new { message = "Monthly assessment saved." });
    }

    private async Task<HashSet<Guid>> ResolveDirectSubjectIdsAsync(string identityUserId, Guid assessorPersonId, string? assessorJobTitle, CancellationToken ct)
    {
        var callerRank = AttendanceRoleRank(assessorJobTitle);
        if (callerRank <= 100) return [];
        var scope = await _dataScope.ResolveAsync(identityUserId, ct);
        var candidates = await _db.Persons.AsNoTracking()
            .Where(person => scope.PersonIds.Contains(person.PersonId) && person.PersonId != assessorPersonId && person.Staff != null && person.Staff.Vacancy != null)
            .Select(person => new { person.PersonId, JobTitle = person.Staff!.Vacancy!.JobTitleNav != null
                ? person.Staff.Vacancy.JobTitleNav.TitleName : person.Staff.Vacancy.JobTitle }).ToListAsync(ct);
        var lower = candidates.Select(person => new { person.PersonId, Rank = AttendanceRoleRank(person.JobTitle) })
            .Where(person => person.Rank > 0 && person.Rank < callerRank).ToList();
        if (lower.Count == 0) return [];
        var directRank = lower.Max(person => person.Rank);
        return lower.Where(person => person.Rank == directRank).Select(person => person.PersonId).ToHashSet();
    }

    public sealed class SaveAssessmentDto { public int Year { get; set; } public int Month { get; set; } public int Rating { get; set; } public string Remarks { get; set; } = string.Empty; }

    private sealed class HierarchyStaffRow
    {
        public Guid PersonId { get; init; }
        public int TenantId { get; init; }
        public int? OrganizationId { get; init; }
        public Guid StaffGuid { get; init; }
        public string StaffId { get; init; } = string.Empty;
        public string FullName { get; init; } = string.Empty;
        public string? Department { get; init; }
        public string? JobTitle { get; init; }
        public int HierarchyLevel { get; set; }
    }

    private static int AttendanceRoleRank(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return 0;
        var value = new string(title.Trim().ToLowerInvariant()
            .Where(char.IsLetterOrDigit).ToArray());

        var isDutyCeo = value.Contains("dutyceo");
        var isDeputyManager = value.Contains("deputymanager") || value.Contains("deptymanager");
        var isAssistantManager = value.Contains("assistantmanager") ||
                                 value.Contains("asstmanager") ||
                                 value.Contains("assistmanager");

        if (!isDutyCeo && (value.Contains("ceo") || value.Contains("chiefexecutive"))) return 700;
        if (isDutyCeo) return 600;
        if (!isDeputyManager && !isAssistantManager && value.Contains("manager")) return 500;
        if (isDeputyManager) return 400;
        if (isAssistantManager) return 300;
        if (value.Contains("supervisor") || value.Contains("teamlead")) return 200;
        if (value.Contains("agent") || value.Contains("bellboy")) return 100;
        return 0;
    }
}
