using Accounts.Data;
using Accounts.Idempotency;
using Accounts.Models;
using Accounts.Models.SpListRows;
using Accounts.Services.Interfaces;
using Accounts.Services.Services;
using Accounts.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Data;
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
    private readonly PayrollCalculationService _payroll;

    public AssessmentController(
        ApplicationDbContext db,
        RbacService rbac,
        IOrganizationDataScopeService dataScope,
        ITenantService tenant,
        TenantPermissionService tenantPermissions,
        AssessmentSchedulerService assessmentScheduler,
        PayrollCalculationService payroll)
    {
        _db = db;
        _rbac = rbac;
        _dataScope = dataScope;
        _tenant = tenant;
        _tenantPermissions = tenantPermissions;
        _assessmentScheduler = assessmentScheduler;
        _payroll = payroll;
    }

    [HttpGet("final")]
    public async Task<IActionResult> GetFinalList([FromQuery] int? year, [FromQuery] int? month, CancellationToken ct)
    {
        var identityUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(identityUserId)) return Unauthorized();

        var isSuperAdmin = User.IsInRole("SuperAdmin") || string.Equals(
            User.FindFirstValue(ITenantService.ClaimIsSuperAdmin), "true", StringComparison.OrdinalIgnoreCase);
        var isTenantAdmin = User.IsInRole("TenantAdmin") || string.Equals(
            User.FindFirstValue(ITenantService.ClaimIsTenantAdmin), "true", StringComparison.OrdinalIgnoreCase);

        if (isSuperAdmin)
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                message = "Final Assessment contains tenant staff data. Open it with a tenant-scoped authorized account."
            });
        if (!_tenant.TenantId.HasValue) return Forbid();

        if (isTenantAdmin)
        {
            if (!await _tenantPermissions.HasMenuRouteAsync(User, ["/assessment/final"], "VIEW", ct))
                return Forbid();
        }
        else
        {
            var staffId = await _db.Persons.AsNoTracking()
                .Where(person => person.IdentityUserId == identityUserId && person.Staff != null)
                .Select(person => (Guid?)person.Staff!.StaffId)
                .FirstOrDefaultAsync(ct);
            if (!staffId.HasValue ||
                !await HasStaffMenuActionAsync(staffId.Value, "/assessment/final", "VIEW", ct))
                return Forbid();
        }

        var assessmentYear = year is >= 2000 and <= 2100 ? year.Value : DateTime.Today.Year;
        var assessmentMonth = month is >= 1 and <= 12 ? month.Value : DateTime.Today.Month;
        await AssessmentSchema.EnsureCurrentAsync(_db);

        var tenantId = _tenant.TenantId.Value;
        var people = await _db.Persons.AsNoTracking()
            .Where(person => person.TenantId == tenantId && person.IsActive && person.Staff != null)
            .Select(person => new
            {
                person.PersonId,
                StaffGuid = person.Staff!.StaffId,
                StaffId = person.Staff.LoginId ?? person.Staff.StaffId.ToString(),
                person.FullName,
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

        var assessments = await _db.StaffAssessments.AsNoTracking()
            .Where(item => item.TenantId == tenantId &&
                           item.AssessmentYear == assessmentYear &&
                           item.AssessmentMonth == assessmentMonth)
            .ToListAsync(ct);
        var latestByPerson = assessments
            .GroupBy(item => item.SubjectPersonId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(item => item.IsLocked || item.Rating != null)
                    .ThenByDescending(item => item.SubmittedDateUtc ?? item.ModifiedDateUtc ?? item.CreatedDateUtc)
                    .ThenByDescending(item => item.Id)
                    .First());

        var assessorIds = latestByPerson.Values.Select(item => item.AssessorPersonId).Distinct().ToList();
        var assessorNames = await _db.Persons.AsNoTracking()
            .Where(person => person.TenantId == tenantId && assessorIds.Contains(person.PersonId))
            .ToDictionaryAsync(person => person.PersonId, person => person.FullName, ct);

        var orderedPeople = people
            .OrderBy(person => person.Department ?? string.Empty)
            .ThenBy(person => person.FullName)
            .ToList();
        var rows = orderedPeople.Select((person, index) =>
        {
            latestByPerson.TryGetValue(person.PersonId, out var assessment);
            var submitted = assessment != null && (assessment.IsLocked || assessment.Rating.HasValue);
            var posted = assessment?.IsFinalApproved == true && assessment.IsPostedToPayroll;
            return new AssessmentFinalListRow
            {
                Id = index + 1,
                AssessmentId = assessment?.Id,
                PersonId = person.PersonId,
                StaffGuid = person.StaffGuid,
                StaffId = person.StaffId,
                FullName = person.FullName,
                Department = person.Department ?? "-",
                JobTitle = person.JobTitle ?? "-",
                AssessmentYear = assessmentYear,
                AssessmentMonth = assessmentMonth,
                Rating = assessment?.Rating,
                Amount = assessment?.Amount,
                Remarks = assessment?.Remarks,
                IsLocked = submitted,
                SubmittedDateUtc = assessment?.SubmittedDateUtc,
                AssessorPersonId = assessment?.AssessorPersonId,
                AssessorName = assessment != null && assessorNames.TryGetValue(assessment.AssessorPersonId, out var assessorName)
                    ? assessorName
                    : "-",
                IsFinalApproved = assessment?.IsFinalApproved ?? false,
                FinalApprovedByName = assessment?.FinalApprovedByName,
                FinalApprovedDateUtc = assessment?.FinalApprovedDateUtc,
                IsPostedToPayroll = assessment?.IsPostedToPayroll ?? false,
                PostedPayrollRunId = assessment?.PostedPayrollRunId,
                PostedToPayrollDateUtc = assessment?.PostedToPayrollDateUtc,
                Status = assessment == null
                    ? "Pending"
                    : posted
                        ? "Approved & Posted"
                        : submitted ? "Submitted" : "Open"
            };
        }).ToList();

        return Ok(rows);
    }

    [HttpPut("final/{subjectPersonId:guid}")]
    public async Task<IActionResult> AdjustFinalAmount(
        Guid subjectPersonId,
        [FromBody] AdjustFinalAssessmentDto dto,
        CancellationToken ct)
    {
        if (dto.Year is < 2000 or > 2100 || dto.Month is < 1 or > 12)
            return BadRequest(new { message = "Valid month and year are required." });
        if (dto.Amount < 0)
            return BadRequest(new { message = "Amount cannot be negative." });

        var remarks = string.IsNullOrWhiteSpace(dto.Remarks) ? null : dto.Remarks.Trim();
        if (remarks is { Length: > 500 })
            return BadRequest(new { message = "Remarks cannot exceed 500 characters." });

        var access = await EnsureFinalEditAccessAsync(ct);
        if (access.Result != null) return access.Result;
        var tenantId = access.Context!.Value.TenantId;
        await AssessmentSchema.EnsureCurrentAsync(_db);
        var amount = Math.Round(dto.Amount, 2, MidpointRounding.AwayFromZero);
        var now = DateTime.UtcNow;

        var assessment = await _db.StaffAssessments
            .Where(item => item.TenantId == tenantId &&
                           item.SubjectPersonId == subjectPersonId &&
                           item.AssessmentYear == dto.Year &&
                           item.AssessmentMonth == dto.Month)
            .OrderByDescending(item => item.IsLocked || item.Rating != null)
            .ThenByDescending(item => item.SubmittedDateUtc ?? item.ModifiedDateUtc ?? item.CreatedDateUtc)
            .ThenByDescending(item => item.Id)
            .FirstOrDefaultAsync(ct);

        if (assessment == null || !assessment.IsLocked || !assessment.Rating.HasValue || !assessment.Amount.HasValue)
            return Conflict(new { message = "This employee has not submitted a marked assessment for the selected month." });
        if (assessment.IsFinalApproved || assessment.IsPostedToPayroll)
            return Conflict(new { message = "This assessment is already finally approved and posted to payroll; it can no longer be changed." });
        if (assessment.Amount.Value != amount && string.IsNullOrWhiteSpace(remarks))
            return BadRequest(new { message = "Remarks are required when changing the assessment amount." });

        assessment.Amount = amount;
        assessment.Remarks = remarks;
        assessment.ModifiedDateUtc = now;

        await _db.SaveChangesAsync(ct);
        return Ok(new
        {
            message = "Assessment amount updated.",
            personId = subjectPersonId,
            amount,
            remarks
        });
    }

    [HttpPost("final/pay")]
    [Idempotent]
    public async Task<IActionResult> PayFinalToPayroll([FromBody] PayFinalAssessmentDto dto, CancellationToken ct)
    {
        if (dto.Year is < 2000 or > 2100 || dto.Month is < 1 or > 12)
            return BadRequest(new { message = "Valid month and year are required." });

        var access = await EnsureFinalApproveAccessAsync(ct);
        if (access.Result != null) return access.Result;
        var tenantId = access.Context!.Value.TenantId;
        var approverUserId = access.Context.Value.UserId;

        var approverStaffId = await _db.Persons.AsNoTracking()
            .Where(person => person.IdentityUserId == approverUserId && person.Staff != null)
            .Select(person => (Guid?)person.Staff!.StaffId)
            .FirstOrDefaultAsync(ct);
        var pinAuthority = approverStaffId.HasValue
            ? await _db.ProcessActionAuthorities.AsNoTracking().SingleOrDefaultAsync(authority =>
                authority.StaffId == approverStaffId.Value && authority.ProcessCode == "ASSESSMENT" &&
                authority.ActionCode == "PAY" && authority.IsActive, ct)
            : null;
        if (string.IsNullOrWhiteSpace(pinAuthority?.PinHash))
            return Conflict(new { message = "Configure your Final Assessment Pay PIN in Workflow Authorities before approval." });
        if (!ProcessAuthorityPinHasher.Verify(dto.PinCode?.Trim() ?? string.Empty, pinAuthority.PinHash))
            return BadRequest(new { message = "Invalid Final Assessment security PIN." });

        await AssessmentSchema.EnsureCurrentAsync(_db);

        var draftRun = await _db.PayrollRuns
            .FirstOrDefaultAsync(run => run.TenantId == tenantId &&
                                        run.Year == dto.Year &&
                                        run.Month == dto.Month &&
                                        run.Status == "Draft", ct);
        if (draftRun == null)
            return Conflict(new
            {
                message = $"No Draft payroll exists for {dto.Month:D2}/{dto.Year}. Open Payroll and Create/Recalculate Draft first, then Pay again."
            });

        var assessments = await _db.StaffAssessments
            .Where(item => item.TenantId == tenantId &&
                           item.AssessmentYear == dto.Year &&
                           item.AssessmentMonth == dto.Month &&
                           item.IsLocked &&
                           item.Rating != null &&
                           item.Amount != null)
            .ToListAsync(ct);

        var finalAssessments = assessments
            .GroupBy(item => item.SubjectPersonId)
            .Select(group => group
                .OrderByDescending(item => item.SubmittedDateUtc ?? item.ModifiedDateUtc ?? item.CreatedDateUtc)
                .First())
            .ToList();
        if (finalAssessments.Count == 0)
            return Conflict(new { message = "No submitted assessments are available for final approval in the selected month." });

        var approverName = await _db.Persons.AsNoTracking()
            .Where(person => person.IdentityUserId == approverUserId)
            .Select(person => person.FullName)
            .FirstOrDefaultAsync(ct) ?? User.Identity?.Name ?? approverUserId;
        var lines = await _db.PayrollLines
            .Include(line => line.PayrollRun)
            .Where(line => line.PayrollRunId == draftRun.Id)
            .ToListAsync(ct);

        if (lines.Count == 0)
        {
            draftRun = await _payroll.GenerateAsync(
                approverUserId,
                approverName,
                dto.Year,
                dto.Month,
                draftRun.PayDate,
                ct);
            lines = draftRun.Lines.ToList();
        }
        if (lines.Count == 0)
            return Conflict(new { message = "No active employee payroll lines could be generated for this month." });

        var taxYear = dto.Month >= 7 ? $"{dto.Year}-{dto.Year + 1}" : $"{dto.Year - 1}-{dto.Year}";
        var taxSlabs = await _db.PayrollTaxSlabs.AsNoTracking()
            .Where(slab => slab.IsActive && slab.TaxYear == taxYear)
            .OrderBy(slab => slab.FromAmount)
            .ToListAsync(ct);

        var lineByPerson = lines.ToDictionary(line => line.PersonId);
        var approvedAt = DateTime.UtcNow;
        var approved = 0;
        foreach (var assessment in finalAssessments)
        {
            if (!lineByPerson.ContainsKey(assessment.SubjectPersonId)) continue;
            if (!assessment.IsFinalApproved)
            {
                approved++;
                assessment.IsFinalApproved = true;
                assessment.FinalApprovedByUserId = approverUserId;
                assessment.FinalApprovedByName = approverName;
                assessment.FinalApprovedDateUtc = approvedAt;
            }
            if (!assessment.IsPostedToPayroll)
            {
                assessment.IsPostedToPayroll = true;
                assessment.PostedPayrollRunId = draftRun.Id;
                assessment.PostedToPayrollDateUtc = approvedAt;
            }
            assessment.ModifiedDateUtc = approvedAt;
        }

        var approvedAmountByPerson = finalAssessments
            .Where(assessment => assessment.IsFinalApproved && assessment.IsPostedToPayroll &&
                                 lineByPerson.ContainsKey(assessment.SubjectPersonId))
            .ToDictionary(assessment => assessment.SubjectPersonId, assessment => assessment.Amount!.Value);

        var updated = 0;
        foreach (var line in lines)
        {
            approvedAmountByPerson.TryGetValue(line.PersonId, out var assessmentAmount);
            if (line.AssessmentAmount == assessmentAmount) continue;
            line.AssessmentAmount = assessmentAmount;
            PayrollCalculationService.Recalculate(line);
            line.TaxAmount = PayrollTaxCalculator.CalculateMonthlyTax(line.TaxableIncome, taxSlabs);
            PayrollCalculationService.Recalculate(line);
            updated++;
        }

        draftRun.UpdatedOnUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _payroll.RecalculateRunTotalsAsync(draftRun.Id, ct);

        return Ok(new
        {
            message = updated == 0
                ? "Final Assessment is approved; Draft payroll already has the approved amounts."
                : $"Finally approved and posted {updated} assessment amount(s) into Draft payroll for {dto.Month:D2}/{dto.Year}.",
            updatedLines = updated,
            assessmentCount = approvedAmountByPerson.Count,
            approvedCount = approved,
            year = dto.Year,
            month = dto.Month
        });
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

        if (!isTenantAdmin && current == null) return Ok(Array.Empty<object>());
        var directSubjectIds = isTenantAdmin
            ? null
            : await ResolveDirectSubjectIdsAsync(identityUserId, current!.PersonId, current.JobTitle, ct);

        var peopleQuery = _db.Persons.AsNoTracking()
            .Where(person => person.IsActive && person.Staff != null);
        if (!isTenantAdmin)
            peopleQuery = peopleQuery.Where(person => directSubjectIds!.Contains(person.PersonId));

        var people = await peopleQuery
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

        var alternativeSubjectIds = current == null ? new HashSet<Guid>() : await _db.Persons.AsNoTracking()
            .Where(person => person.IsActive && person.AlternativeReportsToPersonId == current.PersonId &&
                person.ReportsToPersonId != current.PersonId)
            .Select(person => person.PersonId).ToHashSetAsync(ct);

        var visible = people
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
        var visibleIds = visible.Select(person => person.PersonId).ToHashSet();
        var saved = await _db.StaffAssessments.AsNoTracking()
            .Where(item => item.TenantId == tenantId.Value && item.AssessmentYear == assessmentYear &&
                           item.AssessmentMonth == assessmentMonth && visibleIds.Contains(item.SubjectPersonId))
            .Select(item => new { item.Id, item.SubjectPersonId, item.AssessorPersonId, item.Rating, item.Amount, item.IsLocked, item.SubmittedDateUtc })
            .ToListAsync(ct);
        var savedByPerson = saved.GroupBy(item => item.SubjectPersonId)
            .ToDictionary(group => group.Key, group => group
                .OrderByDescending(item => item.IsLocked || item.Rating.HasValue)
                .ThenByDescending(item => item.SubmittedDateUtc)
                .ThenByDescending(item => item.Id).First());
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
            departmentKey = AssessmentDepartmentKey(person.Department, person.OrganizationId),
            jobTitle = person.JobTitle ?? "—",
            person.HierarchyLevel,
            reportingRole = alternativeSubjectIds.Contains(person.PersonId) ? "Alternative" : "Primary",
            submittedByAnotherReporter = savedByPerson.GetValueOrDefault(person.PersonId)?.Rating != null &&
                savedByPerson.GetValueOrDefault(person.PersonId)?.AssessorPersonId != current?.PersonId,
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
    public Task<IActionResult> Save(Guid subjectPersonId, [FromBody] SaveAssessmentDto dto, CancellationToken ct) =>
        SaveAll(new SaveAssessmentsDto
        {
            Year = dto.Year,
            Month = dto.Month,
            Items = [new SaveAssessmentItemDto { SubjectPersonId = subjectPersonId, Rating = dto.Rating }]
        }, ct);

    [HttpPut("staff/bulk")]
    [Idempotent]
    public async Task<IActionResult> SaveAll([FromBody] SaveAssessmentsDto dto, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Unauthorized();
        if (dto.Year is < 2000 or > 2100 || dto.Month is < 1 or > 12 || dto.Items is null || dto.Items.Count is < 1 or > 500)
            return BadRequest(new { message = "Valid month, year and between 1 and 500 assessment rows are required." });
        if (dto.Items.Any(item => item.SubjectPersonId == Guid.Empty || item.Rating is < 1 or > 255))
            return BadRequest(new { message = "Every assessment requires a staff member and a position from 1 to 255." });
        if (dto.Items.Select(item => item.SubjectPersonId).Distinct().Count() != dto.Items.Count)
            return BadRequest(new { message = "The same staff member cannot appear more than once in Save All." });

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

        var requestedSubjectIds = dto.Items.Select(item => item.SubjectPersonId).ToHashSet();
        var allowed = await ResolveDirectSubjectIdsAsync(userId, assessor.PersonId, assessor.JobTitle, ct);
        if (!requestedSubjectIds.IsSubsetOf(allowed)) return Forbid();

        // Positions are ranked within the employee's department, not across every
        // department visible to a primary or alternative reporter.
        var departmentRows = await _db.Persons.AsNoTracking()
            .Where(person => person.TenantId == assessor.TenantId && person.Staff != null)
            .Select(person => new
            {
                person.PersonId,
                OrganizationId = person.Staff!.Vacancy != null ? (int?)person.Staff.Vacancy.OrganizationId : null,
                Department = person.Staff.Vacancy != null
                    ? (person.Staff.Vacancy.Organization != null && person.Staff.Vacancy.Organization.Label == "Department"
                        ? person.Staff.Vacancy.Organization.Name : person.Staff.Vacancy.Department)
                    : null
            }).ToListAsync(ct);
        var departmentByPerson = departmentRows.ToDictionary(
            person => person.PersonId,
            person => AssessmentDepartmentKey(person.Department, person.OrganizationId));
        if (requestedSubjectIds.Any(id => !departmentByPerson.ContainsKey(id))) return Forbid();
        var duplicateInRequest = dto.Items.GroupBy(item =>
            (Department: departmentByPerson[item.SubjectPersonId], item.Rating))
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateInRequest != null)
            return Conflict(new { message = $"Position {duplicateInRequest.Key.Rating} is repeated in {duplicateInRequest.Key.Department}. Give each employee in that department a unique position." });

        IActionResult result;
        try
        {
            var strategy = _db.Database.CreateExecutionStrategy();
            result = await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
                var periodAssessments = await _db.StaffAssessments
                    .Where(item => item.TenantId == assessor.TenantId && item.AssessmentYear == dto.Year &&
                        item.AssessmentMonth == dto.Month)
                    .ToListAsync(ct);

                if (periodAssessments.Any(item => requestedSubjectIds.Contains(item.SubjectPersonId) &&
                    (item.IsLocked || item.Rating.HasValue)))
                    return Conflict(new { message = "One or more employees were already assessed by their primary or alternative reporter. Refresh the grid; no rows were saved." });

                var requestedPositions = dto.Items.Select(item => item.Rating).ToHashSet();
                var conflictingPosition = periodAssessments.FirstOrDefault(item =>
                    item.Rating.HasValue && requestedPositions.Contains(item.Rating.Value) &&
                    departmentByPerson.TryGetValue(item.SubjectPersonId, out var department) &&
                    dto.Items.Any(request => request.Rating == item.Rating.Value &&
                        departmentByPerson[request.SubjectPersonId] == department));
                if (conflictingPosition != null)
                    return Conflict(new { message = $"Position {conflictingPosition.Rating} is already assigned in {departmentByPerson[conflictingPosition.SubjectPersonId]}. No rows were saved." });

                var ownPending = periodAssessments
                    .Where(item => item.AssessorPersonId == assessor.PersonId &&
                        requestedSubjectIds.Contains(item.SubjectPersonId))
                    .ToDictionary(item => item.SubjectPersonId);
                var submittedAt = DateTime.UtcNow;
                foreach (var item in dto.Items)
                {
                    var amount = Math.Max(rule.MinimumBonusAmount, rule.BonusAmount - ((item.Rating - 1) * rule.DecrementAmount));
                    if (!ownPending.TryGetValue(item.SubjectPersonId, out var assessment))
                    {
                        assessment = new StaffAssessment
                        {
                            TenantId = assessor.TenantId,
                            AssessorPersonId = assessor.PersonId,
                            SubjectPersonId = item.SubjectPersonId,
                            AssessmentYear = dto.Year,
                            AssessmentMonth = (byte)dto.Month,
                            CreatedDateUtc = submittedAt
                        };
                        _db.StaffAssessments.Add(assessment);
                    }
                    assessment.Rating = (byte)item.Rating;
                    assessment.Amount = amount;
                    assessment.IsLocked = true;
                    assessment.SubmittedDateUtc = submittedAt;
                    assessment.ModifiedDateUtc = submittedAt;
                }

                await _db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return Ok(new { message = $"{dto.Items.Count} monthly assessment(s) saved and locked.", savedCount = dto.Items.Count });
            });
        }
        catch (DbUpdateException)
        {
            return Conflict(new { message = "An employee or department position was submitted by another request. Refresh the grid; no rows were saved." });
        }
        if (result is not OkObjectResult) return result;

        var submittedSubjects = await _db.StaffAssessments.AsNoTracking()
            .Where(item => item.TenantId == assessor.TenantId && item.AssessmentYear == dto.Year &&
                item.AssessmentMonth == dto.Month && item.Rating.HasValue)
            .Select(item => item.SubjectPersonId).ToHashSetAsync(ct);
        var incomplete = await _db.StaffAssessments.AsNoTracking().AnyAsync(item =>
            item.TenantId == assessor.TenantId && item.AssessorPersonId == assessor.PersonId &&
            item.AssessmentYear == dto.Year && item.AssessmentMonth == dto.Month &&
            item.Rating == null && !item.IsLocked && !submittedSubjects.Contains(item.SubjectPersonId), ct);
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

        return result;
    }

    private static string AssessmentDepartmentKey(string? department, int? organizationId) =>
        !string.IsNullOrWhiteSpace(department)
            ? department.Trim().ToUpperInvariant()
            : $"ORG:{organizationId?.ToString() ?? "UNASSIGNED"}";

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
            !await _tenantPermissions.HasMenuRouteAsync(User, ["/assessment/mark", "/assessment/final"], "VIEW", ct))
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
        if (normalizedAction == "VIEW" && await _rbac.HasAccessAsync(staffId, $"MENU_{menuId.Value}"))
            return true;
        return await _rbac.HasAccessAsync(staffId, $"MENU_{menuId.Value}_{normalizedAction}");
    }

    public sealed class SaveAssessmentDto { public int Year { get; set; } public int Month { get; set; } public int Rating { get; set; } }
    public sealed class SaveAssessmentsDto
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public List<SaveAssessmentItemDto> Items { get; set; } = [];
    }
    public sealed class SaveAssessmentItemDto { public Guid SubjectPersonId { get; set; } public int Rating { get; set; } }
    public sealed class SetScheduleDto { public DateOnly OpenDate { get; set; } }
    public sealed class AdjustFinalAssessmentDto
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public decimal Amount { get; set; }
        public string? Remarks { get; set; }
    }
    public sealed class PayFinalAssessmentDto { public int Year { get; set; } public int Month { get; set; } public string PinCode { get; set; } = string.Empty; }

    private async Task<(IActionResult? Result, (int TenantId, Guid ActorPersonId)? Context)> EnsureFinalEditAccessAsync(
        CancellationToken ct)
    {
        var identityUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(identityUserId)) return (Unauthorized(), null);
        if (!_tenant.TenantId.HasValue) return (Forbid(), null);

        var isSuperAdmin = User.IsInRole("SuperAdmin") || string.Equals(
            User.FindFirstValue(ITenantService.ClaimIsSuperAdmin), "true", StringComparison.OrdinalIgnoreCase);
        if (isSuperAdmin) return (Forbid(), null);

        var isTenantAdmin = User.IsInRole("TenantAdmin") || string.Equals(
            User.FindFirstValue(ITenantService.ClaimIsTenantAdmin), "true", StringComparison.OrdinalIgnoreCase);

        if (isTenantAdmin)
        {
            if (!await _tenantPermissions.HasMenuRouteAsync(User, ["/assessment/final"], "EDIT", ct))
                return (Forbid(), null);

            var adminPersonId = await _db.Persons.AsNoTracking()
                .Where(person => person.IdentityUserId == identityUserId)
                .Select(person => (Guid?)person.PersonId)
                .FirstOrDefaultAsync(ct);
            return (null, (_tenant.TenantId.Value, adminPersonId ?? Guid.Empty));
        }

        var actor = await _db.Persons.AsNoTracking()
            .Where(person => person.IdentityUserId == identityUserId && person.Staff != null)
            .Select(person => new { person.PersonId, StaffId = person.Staff!.StaffId })
            .FirstOrDefaultAsync(ct);
        if (actor == null ||
            !await HasStaffMenuActionAsync(actor.StaffId, "/assessment/final", "EDIT", ct))
            return (Forbid(), null);

        return (null, (_tenant.TenantId.Value, actor.PersonId));
    }

    private async Task<(IActionResult? Result, (int TenantId, string UserId)? Context)> EnsureFinalApproveAccessAsync(
        CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return (Unauthorized(), null);
        if (!_tenant.TenantId.HasValue || TenantPermissionService.IsSuperAdmin(User)) return (Forbid(), null);

        if (TenantPermissionService.IsTenantAdmin(User))
        {
            var hasMenuPermission =
                await _tenantPermissions.HasMenuRouteAsync(User, ["/assessment/final"], "APPROVE", ct) ||
                await _tenantPermissions.HasMenuRouteAsync(User, ["/assessment/final"], "EDIT", ct);
            var adminStaffId = await _db.Persons.AsNoTracking()
                .Where(person => person.IdentityUserId == userId && person.Staff != null)
                .Select(person => (Guid?)person.Staff!.StaffId)
                .FirstOrDefaultAsync(ct);
            return hasMenuPermission && adminStaffId.HasValue &&
                   await HasProcessActionAuthorityAsync(adminStaffId.Value, "ASSESSMENT", "PAY", ct)
                ? (null, (_tenant.TenantId.Value, userId))
                : (StatusCode(StatusCodes.Status403Forbidden, new
                {
                    message = "You are not assigned as the Final Assessment Pay authority. Configure the assignment in Workflow Authorities."
                }), null);
        }

        var staffId = await _db.Persons.AsNoTracking()
            .Where(person => person.IdentityUserId == userId && person.Staff != null)
            .Select(person => (Guid?)person.Staff!.StaffId)
            .FirstOrDefaultAsync(ct);
        if (!staffId.HasValue ||
            (!await HasStaffMenuActionAsync(staffId.Value, "/assessment/final", "APPROVE", ct) &&
             !await HasStaffMenuActionAsync(staffId.Value, "/assessment/final", "EDIT", ct)) ||
            !await HasProcessActionAuthorityAsync(staffId.Value, "ASSESSMENT", "PAY", ct))
            return (Forbid(), null);
        return (null, (_tenant.TenantId.Value, userId));
    }

    private Task<bool> HasProcessActionAuthorityAsync(
        Guid staffId,
        string processCode,
        string actionCode,
        CancellationToken ct) =>
        _db.ProcessActionAuthorities.AsNoTracking().AnyAsync(authority =>
            authority.TenantId == _tenant.RequiredTenantId &&
            authority.StaffId == staffId &&
            authority.ProcessCode == processCode &&
            authority.ActionCode == actionCode &&
            authority.IsActive,
            ct);

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

