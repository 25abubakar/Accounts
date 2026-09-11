using Accounts.Data;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Accounts.Services.Services;
using Accounts.Services;
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
    private readonly TenantPermissionService _tenantPermissions;
    private readonly AssessmentSchedulerService _assessmentScheduler;

    public AssessmentController(
        ApplicationDbContext db,
        RbacService rbac,
        IOrganizationDataScopeService dataScope,
        ITenantService tenant,
        TenantPermissionService tenantPermissions,
        AssessmentSchedulerService assessmentScheduler)
    {
        _db = db;
        _rbac = rbac;
        _dataScope = dataScope;
        _tenant = tenant;
        _tenantPermissions = tenantPermissions;
        _assessmentScheduler = assessmentScheduler;
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

        if (isSuperAdmin) return Ok(Array.Empty<object>());
        if (isTenantAdmin &&
            !await _tenantPermissions.HasMenuRouteAsync(User, ["/assessment/mark"], "VIEW", ct))
            return Forbid();

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
                    ? (person.Staff.Vacancy.DesignationNav != null
                        ? person.Staff.Vacancy.DesignationNav.Name
                        : person.Staff.Vacancy.JobTitle)
                    : null,
                AttendanceScope = person.Staff.Vacancy != null && person.Staff.Vacancy.DesignationNav != null
                    ? person.Staff.Vacancy.DesignationNav.AttendanceVisibilityScope
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
                    ? (person.Staff.Vacancy.DesignationNav != null
                        ? person.Staff.Vacancy.DesignationNav.Name
                        : person.Staff.Vacancy.JobTitle)
                    : null
            })
            .ToListAsync(ct);

        if (!isTenantAdmin && current == null) return Ok(Array.Empty<object>());
        var directSubjectIds = isTenantAdmin ? people.Select(person => person.PersonId).ToHashSet()
            : await ResolveDirectSubjectIdsAsync(identityUserId, current!.PersonId, current.JobTitle, ct);
        var visible = people.Where(person => directSubjectIds.Contains(person.PersonId))
            .OrderBy(person => person.Department).ThenBy(person => person.FullName).ToList();
        var assessmentYear = year is >= 2000 and <= 2100 ? year.Value : DateTime.Today.Year;
        var assessmentMonth = month is >= 1 and <= 12 ? month.Value : DateTime.Today.Month;
        var tenantId = current?.TenantId ?? _tenant.TenantId;
        if (!tenantId.HasValue) return Ok(Array.Empty<object>());
        await AssessmentSchema.EnsureCurrentAsync(_db);
        var today = DateOnly.FromDateTime(PakistanClock.Now());
        var activeRule = await _db.AssessmentBonusRules.AsNoTracking().FirstOrDefaultAsync(x => x.IsActive, ct);
        var schedule = await _db.AssessmentSchedules.AsNoTracking()
            .FirstOrDefaultAsync(x => x.AssessmentYear == assessmentYear && x.AssessmentMonth == assessmentMonth && x.IsActive, ct);
        var configuredOpenDay = schedule?.OpenDay ?? activeRule?.OpenDay ?? 25;
        var configuredCloseDay = schedule?.CloseDay ?? activeRule?.CloseDay ?? 8;
        var cycle = AssessmentCycleWindow.Create(assessmentYear, assessmentMonth, configuredOpenDay, configuredCloseDay);
        var windowOpen = activeRule != null && cycle.Contains(today);
        var saved = await _db.StaffAssessments.AsNoTracking()
            .Where(item => item.TenantId == tenantId.Value && item.AssessmentYear == assessmentYear &&
                           item.AssessmentMonth == assessmentMonth &&
                           (isTenantAdmin || item.AssessorPersonId == current!.PersonId))
            .Select(item => new { item.SubjectPersonId, item.Rating, item.Amount, item.IsLocked, item.SubmittedDateUtc })
            .ToListAsync(ct);
        var savedByPerson = saved.GroupBy(item => item.SubjectPersonId)
            .ToDictionary(group => group.Key, group => group.First());
        var canEditAssessments = !isTenantAdmin && current != null &&
            await HasStaffMenuActionAsync(current.StaffId, "/assessment/mark", "EDIT", ct);
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
            amount = savedByPerson.GetValueOrDefault(person.PersonId)?.Amount,
            bonusAmount = savedByPerson.GetValueOrDefault(person.PersonId)?.Amount,
            canEdit = canEditAssessments && windowOpen && savedByPerson.GetValueOrDefault(person.PersonId)?.Rating == null && savedByPerson.GetValueOrDefault(person.PersonId)?.IsLocked != true,
            assessmentWindowOpen = windowOpen,
            openDay = configuredOpenDay,
            closeDay = configuredCloseDay,
            openDate = cycle.OpenDate,
            closeDate = cycle.CloseDate,
            isLocked = savedByPerson.GetValueOrDefault(person.PersonId)?.IsLocked == true || savedByPerson.GetValueOrDefault(person.PersonId)?.Rating != null,
            submittedDateUtc = savedByPerson.GetValueOrDefault(person.PersonId)?.SubmittedDateUtc
        }));
    }

    [HttpPut("staff/{subjectPersonId:guid}")]
    public async Task<IActionResult> Save(Guid subjectPersonId, [FromBody] SaveAssessmentDto dto, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Unauthorized();
        if (dto.Year is < 2000 or > 2100 || dto.Month is < 1 or > 12 || dto.Rating is < 1 or > 255)
            return BadRequest(new { message = "Valid month, year and a position from 1 to 255 are required." });
        await AssessmentSchema.EnsureCurrentAsync(_db);
        var today = DateOnly.FromDateTime(PakistanClock.Now());
        var rule = await _db.AssessmentBonusRules.AsNoTracking().FirstOrDefaultAsync(x => x.IsActive, ct);
        if (rule == null) return Conflict(new { message = "Tenant assessment bonus rule is not configured or is inactive." });
        var schedule = await _db.AssessmentSchedules.AsNoTracking()
            .FirstOrDefaultAsync(x => x.AssessmentYear == dto.Year && x.AssessmentMonth == dto.Month && x.IsActive, ct);
        var cycle = AssessmentCycleWindow.Create(dto.Year, dto.Month, schedule?.OpenDay ?? rule.OpenDay, schedule?.CloseDay ?? rule.CloseDay);
        if (!cycle.Contains(today))
            return Conflict(new { message = $"Assessment entry is available from {cycle.OpenDate:dd MMM yyyy} through {cycle.CloseDate:dd MMM yyyy}." });
        var assessor = await _db.Persons.AsNoTracking().Where(person => person.IdentityUserId == userId && person.IsActive)
            .Select(person => new { person.PersonId, person.TenantId, StaffId = person.Staff != null ? (Guid?)person.Staff.StaffId : null, JobTitle = person.Staff != null && person.Staff.Vacancy != null
                ? (person.Staff.Vacancy.DesignationNav != null ? person.Staff.Vacancy.DesignationNav.Name : person.Staff.Vacancy.JobTitle) : null })
            .FirstOrDefaultAsync(ct);
        if (assessor == null || !assessor.StaffId.HasValue ||
            !await HasStaffMenuActionAsync(assessor.StaffId.Value, "/assessment/mark", "EDIT", ct))
            return Forbid();
        var amount = Math.Max(rule.MinimumBonusAmount, rule.BonusAmount - ((dto.Rating - 1) * rule.DecrementAmount));
        var allowed = await ResolveDirectSubjectIdsAsync(userId, assessor.PersonId, assessor.JobTitle, ct);
        if (!allowed.Contains(subjectPersonId)) return Forbid();
        var duplicateRank = await _db.StaffAssessments.AsNoTracking().AnyAsync(item =>
            item.TenantId == assessor.TenantId && item.AssessorPersonId == assessor.PersonId &&
            item.SubjectPersonId != subjectPersonId && item.AssessmentYear == dto.Year &&
            item.AssessmentMonth == dto.Month && item.Rating == dto.Rating, ct);
        if (duplicateRank) return Conflict(new { message = $"Position {dto.Rating} is already assigned to another team member for this month." });

        var assessment = await _db.StaffAssessments.AsNoTracking().SingleOrDefaultAsync(item =>
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
                Amount = amount,
                IsLocked = true,
                SubmittedDateUtc = DateTime.UtcNow,
                CreatedDateUtc = DateTime.UtcNow
            };
            _db.StaffAssessments.Add(assessment);
        }
        else
        {
            if (assessment.IsLocked || assessment.Rating.HasValue)
                return Conflict(new { message = "This assessment has already been submitted and is permanently locked." });
            var submittedAt = DateTime.UtcNow;
            var updated = await _db.StaffAssessments
                .Where(item => item.Id == assessment.Id && !item.IsLocked && item.Rating == null)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.Rating, (byte?)dto.Rating)
                    .SetProperty(item => item.Amount, (decimal?)amount)
                    .SetProperty(item => item.IsLocked, true)
                    .SetProperty(item => item.SubmittedDateUtc, submittedAt)
                    .SetProperty(item => item.ModifiedDateUtc, submittedAt), ct);
            if (updated == 0)
                return Conflict(new { message = "This assessment has already been submitted and is permanently locked." });
        }
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return Conflict(new { message = $"Position {dto.Rating} is already assigned or this assessment was submitted by another request." });
        }

        // Keep an already-generated Draft payroll synchronized immediately. Approved
        // or paid payroll is immutable and must never be changed by a later assessment.
        var draftPayrollLines = await _db.PayrollLines.Include(line => line.PayrollRun)
            .Where(line => line.PersonId == subjectPersonId && line.Year == dto.Year &&
                line.Month == dto.Month && line.PayrollRun != null && line.PayrollRun.Status == "Draft")
            .ToListAsync(ct);
        if (draftPayrollLines.Count > 0)
        {
            var taxYear = dto.Month >= 7 ? $"{dto.Year}-{dto.Year + 1}" : $"{dto.Year - 1}-{dto.Year}";
            var taxSlabs = await _db.PayrollTaxSlabs.AsNoTracking()
                .Where(slab => slab.IsActive && slab.TaxYear == taxYear)
                .OrderBy(slab => slab.FromAmount)
                .ToListAsync(ct);
            foreach (var line in draftPayrollLines)
            {
                line.AssessmentAmount = amount;
                PayrollCalculationService.Recalculate(line);
                line.TaxAmount = PayrollTaxCalculator.CalculateMonthlyTax(line.TaxableIncome, taxSlabs);
                PayrollCalculationService.Recalculate(line);
                if (line.PayrollRun != null) line.PayrollRun.UpdatedOnUtc = DateTime.UtcNow;
            }
            await _db.SaveChangesAsync(ct);
        }

        var incomplete = await _db.StaffAssessments.AsNoTracking().AnyAsync(item =>
            item.AssessorPersonId == assessor.PersonId && item.AssessmentYear == dto.Year &&
            item.AssessmentMonth == dto.Month && item.Rating == null && !item.IsLocked, ct);
        if (!incomplete)
        {
            var cycleEntityId = AssessmentReminderEntityId(dto.Year, dto.Month, assessor.PersonId);
            var reminders = await _db.AppNotes
                .Where(note => note.EntityType == "ASSESSMENT_REMINDER" && note.IsActive &&
                    (note.EntityId == cycleEntityId || note.Targets.Any(target => target.IsActive && target.TargetValue == assessor.PersonId.ToString())))
                .ToListAsync(ct);
            foreach (var reminder in reminders)
            {
                reminder.IsActive = false;
                reminder.EndDateUtc = DateTime.UtcNow;
                reminder.UpdatedOnUtc = DateTime.UtcNow;
            }
            if (reminders.Count > 0) await _db.SaveChangesAsync(ct);
        }
        return Ok(new { message = "Monthly assessment saved." });
    }

    private async Task<HashSet<Guid>> ResolveDirectSubjectIdsAsync(string identityUserId, Guid assessorPersonId, string? assessorJobTitle, CancellationToken ct)
    {
        // Explicit reporting assignments are authoritative. This lets a user who
        // has been granted the Assessment tab work on the staff actually assigned
        // to them, without requiring a hard-coded job-title name.
        var assignedReports = await _db.Persons.AsNoTracking()
            .Where(person => person.IsActive && person.PersonId != assessorPersonId &&
                (person.ReportsToPersonId == assessorPersonId ||
                 person.AlternativeReportsToPersonId == assessorPersonId))
            .Select(person => person.PersonId)
            .ToHashSetAsync(ct);
        if (assignedReports.Count > 0) return assignedReports;

        // Legacy fallback for tenants that have not yet configured Reports To.
        var callerRank = AttendanceRoleRank(assessorJobTitle);
        if (callerRank <= 100) return [];
        var scope = await _dataScope.ResolveAsync(identityUserId, ct);
        var candidates = await _db.Persons.AsNoTracking()
            .Where(person => scope.PersonIds.Contains(person.PersonId) && person.PersonId != assessorPersonId && person.Staff != null && person.Staff.Vacancy != null)
            .Select(person => new { person.PersonId, JobTitle = person.Staff!.Vacancy!.DesignationNav != null
                ? person.Staff.Vacancy.DesignationNav.Name : person.Staff.Vacancy.JobTitle }).ToListAsync(ct);
        var lower = candidates.Select(person => new { person.PersonId, Rank = AttendanceRoleRank(person.JobTitle) })
            .Where(person => person.Rank > 0 && person.Rank < callerRank).ToList();
        if (lower.Count == 0) return [];
        var directRank = lower.Max(person => person.Rank);
        return lower.Where(person => person.Rank == directRank).Select(person => person.PersonId).ToHashSet();
    }

    [HttpGet("schedule")]
    public async Task<IActionResult> GetSchedule(CancellationToken ct)
    {
        if (!_tenant.TenantId.HasValue || _tenant.IsSuperAdmin) return Forbid();
        if (TenantPermissionService.IsTenantAdmin(User) &&
            !await _tenantPermissions.HasMenuRouteAsync(User, ["/assessment/mark"], "VIEW", ct))
            return Forbid();
        await AssessmentSchema.EnsureCurrentAsync(_db);
        var today = DateOnly.FromDateTime(PakistanClock.Now());
        var rule = await _db.AssessmentBonusRules.AsNoTracking().FirstOrDefaultAsync(x => x.IsActive, ct);
        var current = await ResolveCycleAsync(today.Year, today.Month, rule, ct);
        var previousMonth = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
        var previous = await ResolveCycleAsync(previousMonth.Year, previousMonth.Month, rule, ct);
        var selected = current.Cycle.Contains(today) ? current : previous.Cycle.Contains(today) ? previous : current;
        return Ok(new
        {
            year = selected.Cycle.Year,
            month = selected.Cycle.Month,
            openDay = selected.Cycle.OpenDate.Day,
            closeDay = selected.Cycle.CloseDate.Day,
            openDate = selected.Cycle.OpenDate,
            closeDate = selected.Cycle.CloseDate,
            isManualOverride = selected.IsManualOverride,
            isOpen = rule != null && selected.Cycle.Contains(today)
        });
    }

    [HttpPut("schedule")]
    public async Task<IActionResult> SetSchedule([FromBody] SetScheduleDto dto, CancellationToken ct)
    {
        if (!_tenant.TenantId.HasValue || !TenantPermissionService.IsTenantAdmin(User) ||
            !await _tenantPermissions.HasMenuRouteAsync(User, ["/assessment/mark"], "EDIT", ct))
            return Forbid();
        await AssessmentSchema.EnsureCurrentAsync(_db);
        var today = DateOnly.FromDateTime(PakistanClock.Now());
        if (dto.OpenDate.Year != today.Year || dto.OpenDate.Month != today.Month || dto.OpenDate.Day > DateTime.DaysInMonth(today.Year, today.Month)) return BadRequest(new { message = "Select a valid date in the running month." });
        var rule = await _db.AssessmentBonusRules.AsNoTracking().FirstOrDefaultAsync(x => x.IsActive, ct);
        if (rule == null) return Conflict(new { message = "Configure and activate the Assessment Rule before opening the cycle." });
        var row = await _db.AssessmentSchedules.SingleOrDefaultAsync(x => x.AssessmentYear == today.Year && x.AssessmentMonth == today.Month, ct);
        if (row == null) { row = new AssessmentSchedule { TenantId = _tenant.TenantId.Value, AssessmentYear = today.Year, AssessmentMonth = (byte)today.Month, CreatedDateUtc = DateTime.UtcNow, CreatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) }; _db.AssessmentSchedules.Add(row); }
        row.OpenDay = (byte)dto.OpenDate.Day; row.CloseDay = rule.CloseDay; row.IsManualOverride = true; row.IsActive = true;
        await _db.SaveChangesAsync(ct);
        await _assessmentScheduler.RunNowAsync(CancellationToken.None);
        var generated = await _db.StaffAssessments.AsNoTracking().CountAsync(x => x.AssessmentYear == today.Year && x.AssessmentMonth == today.Month, ct);
        var cycle = AssessmentCycleWindow.Create(today.Year, today.Month, row.OpenDay, row.CloseDay);
        return Ok(new { message = $"Assessment entry is scheduled from {cycle.OpenDate:dd MMM yyyy} through {cycle.CloseDate:dd MMM yyyy}.", generatedRows = generated });
    }

    private async Task<(AssessmentCycle Cycle, bool IsManualOverride)> ResolveCycleAsync(
        int year,
        int month,
        AssessmentBonusRule? rule,
        CancellationToken ct)
    {
        var schedule = await _db.AssessmentSchedules.AsNoTracking()
            .FirstOrDefaultAsync(x => x.AssessmentYear == year && x.AssessmentMonth == month && x.IsActive, ct);
        var cycle = AssessmentCycleWindow.Create(year, month,
            schedule?.OpenDay ?? rule?.OpenDay ?? 25,
            schedule?.CloseDay ?? rule?.CloseDay ?? 8);
        return (cycle, schedule?.IsManualOverride == true);
    }

    private static string AssessmentReminderEntityId(int year, int month, Guid assessorPersonId) =>
        $"{year:D4}-{month:D2}:{assessorPersonId:N}";

    private async Task<bool> HasStaffMenuActionAsync(
        Guid staffId,
        string route,
        string action,
        CancellationToken ct)
    {
        var menuId = await _db.Menus.AsNoTracking()
            .Where(menu => menu.IsActive && menu.Route == route)
            .Select(menu => (int?)menu.Id)
            .FirstOrDefaultAsync(ct);
        if (!menuId.HasValue) return false;

        var normalizedAction = action.Trim().ToUpperInvariant();
        return await _rbac.HasAccessAsync(staffId, $"MENU_{menuId.Value}_{normalizedAction}");
    }

    public sealed class SaveAssessmentDto { public int Year { get; set; } public int Month { get; set; } public int Rating { get; set; } }
    public sealed class SetScheduleDto { public DateOnly OpenDate { get; set; } }

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

