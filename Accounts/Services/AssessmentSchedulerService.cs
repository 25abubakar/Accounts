using Accounts.Data;
using Accounts.Models;
using Accounts.Services.Services;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services;

public sealed class AssessmentSchedulerService(IServiceScopeFactory scopeFactory, ILogger<AssessmentSchedulerService> logger) : BackgroundService
{
    private readonly SemaphoreSlim _runGate = new(1, 1);
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await RunNowAsync(stoppingToken);
            using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
            while (await timer.WaitForNextTickAsync(stoppingToken)) await RunNowAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    public async Task RunNowAsync(CancellationToken ct = default)
    {
        if (!await _runGate.WaitAsync(0, ct)) return;
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var rbac = scope.ServiceProvider.GetRequiredService<RbacService>();
            await AssessmentSchema.EnsureCurrentAsync(db);
            var today = DateOnly.FromDateTime(PakistanClock.Now());
            var tenants = await db.Tenants.AsNoTracking().Where(x => x.IsActive).Select(x => x.Id).ToListAsync(ct);
            var org = await db.OrganizationTree.IgnoreQueryFilters().AsNoTracking().Select(x => new { x.Id, x.ParentId }).ToListAsync(ct);
            var children = org.Where(x => x.ParentId.HasValue).GroupBy(x => x.ParentId!.Value).ToDictionary(x => x.Key, x => x.Select(y => y.Id).ToList());
            var assessmentMenuId = await db.Menus.AsNoTracking()
                .Where(x => x.IsActive && x.Route == "/assessment/mark")
                .Select(x => (int?)x.Id)
                .FirstOrDefaultAsync(ct);
            if (!assessmentMenuId.HasValue) return;

            foreach (var tenantId in tenants)
            {
                var rule = await db.AssessmentBonusRules.IgnoreQueryFilters().AsNoTracking()
                    .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.IsActive, ct);
                if (rule == null) continue;

                var currentCycle = await ResolveCycleAsync(db, tenantId, today.Year, today.Month, rule, ct);
                var previousMonth = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
                var previousCycle = await ResolveCycleAsync(db, tenantId, previousMonth.Year, previousMonth.Month, rule, ct);
                var cycle = currentCycle.Contains(today) ? currentCycle : previousCycle.Contains(today) ? previousCycle : (AssessmentCycle?)null;
                if (!cycle.HasValue) continue;

                var people = await db.Persons.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == tenantId && x.IsActive && x.IdentityUserId != null && x.Staff != null && x.Staff.Vacancy != null)
                    .Select(x => new PersonRow(x.PersonId, x.IdentityUserId!, x.Staff!.StaffId, x.Staff.Vacancy!.OrganizationId,
                        x.Staff.Vacancy.DesignationNav != null ? x.Staff.Vacancy.DesignationNav.Name : x.Staff.Vacancy.JobTitle,
                        x.ReportsToPersonId, x.AlternativeReportsToPersonId)).ToListAsync(ct);

                foreach (var assessor in people)
                {
                    if (!await rbac.HasAccessAsync(assessor.StaffId, $"MENU_{assessmentMenuId.Value}_EDIT")) continue;
                    var subjects = people.Where(x => x.PersonId != assessor.PersonId &&
                        (x.ReportsToPersonId == assessor.PersonId || x.AlternativeReportsToPersonId == assessor.PersonId)).ToList();
                    if (subjects.Count == 0)
                    {
                        var assessorRank = Rank(assessor.JobTitle);
                        if (assessorRank <= 100) continue;
                        var nodeIds = Descendants(assessor.OrganizationId, children);
                        var lower = people.Where(x => x.PersonId != assessor.PersonId && nodeIds.Contains(x.OrganizationId) && Rank(x.JobTitle) > 0 && Rank(x.JobTitle) < assessorRank).ToList();
                        if (lower.Count == 0) continue;
                        var directRank = lower.Max(x => Rank(x.JobTitle));
                        subjects = lower.Where(x => Rank(x.JobTitle) == directRank).ToList();
                    }
                    var existing = await db.StaffAssessments.IgnoreQueryFilters().Where(x => x.TenantId == tenantId && x.AssessorPersonId == assessor.PersonId && x.AssessmentYear == cycle.Value.Year && x.AssessmentMonth == cycle.Value.Month).ToListAsync(ct);
                    foreach (var subject in subjects.Where(x => existing.All(y => y.SubjectPersonId != x.PersonId)))
                        db.StaffAssessments.Add(new StaffAssessment { TenantId = tenantId, AssessorPersonId = assessor.PersonId, SubjectPersonId = subject.PersonId, AssessmentYear = cycle.Value.Year, AssessmentMonth = (byte)cycle.Value.Month, CreatedDateUtc = DateTime.UtcNow });
                    await db.SaveChangesAsync(ct);

                    var submittedSubjectIds = await db.StaffAssessments.IgnoreQueryFilters().AsNoTracking()
                        .Where(x => x.TenantId == tenantId && x.AssessmentYear == cycle.Value.Year &&
                            x.AssessmentMonth == cycle.Value.Month && x.Rating.HasValue)
                        .Select(x => x.SubjectPersonId).ToHashSetAsync(ct);
                    var incomplete = await db.StaffAssessments.IgnoreQueryFilters().AnyAsync(x =>
                        x.TenantId == tenantId && x.AssessorPersonId == assessor.PersonId &&
                        x.AssessmentYear == cycle.Value.Year && x.AssessmentMonth == cycle.Value.Month &&
                        x.Rating == null && !x.IsLocked && !submittedSubjectIds.Contains(x.SubjectPersonId), ct);
                    var entityId = ReminderEntityId(cycle.Value.Year, cycle.Value.Month, assessor.PersonId);
                    var activeReminders = await db.AppNotes.IgnoreQueryFilters().Include(x => x.Targets)
                        .Where(x => x.TenantId == tenantId && x.EntityType == "ASSESSMENT_REMINDER" && x.IsActive &&
                            x.Targets.Any(target => target.IsActive && target.TargetValue == assessor.PersonId.ToString()))
                        .ToListAsync(ct);
                    var reminder = activeReminders.FirstOrDefault(x => x.EntityId == entityId);
                    var remindersToClose = incomplete
                        ? activeReminders.Where(x => x.EntityId != entityId).ToList()
                        : activeReminders;
                    foreach (var staleReminder in remindersToClose)
                    {
                        staleReminder.IsActive = false;
                        staleReminder.EndDateUtc = DateTime.UtcNow;
                        staleReminder.UpdatedOnUtc = DateTime.UtcNow;
                    }
                    if (!incomplete)
                    {
                        if (remindersToClose.Count > 0) await db.SaveChangesAsync(ct);
                        continue;
                    }
                    if (remindersToClose.Count > 0) await db.SaveChangesAsync(ct);
                    if (reminder != null) continue;
                    var remainingDays = Math.Max(1, cycle.Value.CloseDate.DayNumber - today.DayNumber + 1);
                    var periodName = new DateOnly(cycle.Value.Year, cycle.Value.Month, 1).ToString("MMMM yyyy");
                    db.AppNotes.Add(new AppNote { TenantId = tenantId, Title = "Monthly assessment is pending", NoteBody = $"Please complete your team assessment for {periodName} by {cycle.Value.CloseDate:dd MMM yyyy}.", NoteTypeCode = "NOTIFICATION", SourceTypeCode = "ADMIN", CategoryCode = "ASSESSMENT", PriorityCode = "HIGH", VisibilityTypeCode = "STAFF", MenuCode = "/assessment/mark", ModuleName = "Assessment", EntityType = "ASSESSMENT_REMINDER", EntityId = entityId, StartDateUtc = DateTime.UtcNow, EndDateUtc = DateTime.UtcNow.AddDays(remainingDays), IsPublished = true, IsActive = true, AllowDismiss = true, CreatedBy = "SYSTEM", CreatedOnUtc = DateTime.UtcNow, Targets = [new AppNoteTarget { TargetTypeCode = "STAFF", TargetValue = assessor.PersonId.ToString(), IsActive = true }] });
                    await db.SaveChangesAsync(ct);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex) { logger.LogError(ex, "Assessment scheduler execution failed."); }
        finally { _runGate.Release(); }
    }

    private static HashSet<int> Descendants(int root, Dictionary<int, List<int>> children)
    {
        var result = new HashSet<int>(); var stack = new Stack<int>(); stack.Push(root);
        while (stack.TryPop(out var id)) if (result.Add(id) && children.TryGetValue(id, out var nested)) foreach (var child in nested) stack.Push(child);
        return result;
    }
    private static async Task<AssessmentCycle> ResolveCycleAsync(ApplicationDbContext db, int tenantId, int year, int month, AssessmentBonusRule rule, CancellationToken ct)
    {
        var schedule = await db.AssessmentSchedules.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.AssessmentYear == year && x.AssessmentMonth == month && x.IsActive, ct);
        return AssessmentCycleWindow.Create(year, month, schedule?.OpenDay ?? rule.OpenDay, schedule?.CloseDay ?? rule.CloseDay);
    }
    private static string ReminderEntityId(int year, int month, Guid assessorPersonId) => $"{year:D4}-{month:D2}:{assessorPersonId:N}";
    private static int Rank(string? title) { if (string.IsNullOrWhiteSpace(title)) return 0; var v = new string(title.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray()); if (v.Contains("ceo") && !v.Contains("dutyceo")) return 700; if (v.Contains("dutyceo")) return 600; if (v.Contains("manager") && !v.Contains("deputy") && !v.Contains("depty") && !v.Contains("assistant") && !v.Contains("asst")) return 500; if (v.Contains("deputymanager") || v.Contains("deptymanager")) return 400; if (v.Contains("assistantmanager") || v.Contains("asstmanager")) return 300; if (v.Contains("supervisor") || v.Contains("teamlead")) return 200; if (v.Contains("agent") || v.Contains("bellboy")) return 100; return 0; }
    private sealed record PersonRow(Guid PersonId, string IdentityUserId, Guid StaffId, int OrganizationId, string? JobTitle, Guid? ReportsToPersonId, Guid? AlternativeReportsToPersonId);
}
