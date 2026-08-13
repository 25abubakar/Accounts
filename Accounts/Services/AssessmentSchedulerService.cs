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
            await AssessmentSchema.EnsureCurrentAsync(db);
            var today = DateOnly.FromDateTime(PakistanClock.Now());
            var tenants = await db.Tenants.AsNoTracking().Where(x => x.IsActive).Select(x => x.Id).ToListAsync(ct);
            var org = await db.OrganizationTree.AsNoTracking().Select(x => new { x.Id, x.ParentId }).ToListAsync(ct);
            var children = org.Where(x => x.ParentId.HasValue).GroupBy(x => x.ParentId!.Value).ToDictionary(x => x.Key, x => x.Select(y => y.Id).ToList());

            foreach (var tenantId in tenants)
            {
                var schedule = await db.AssessmentSchedules.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.AssessmentYear == today.Year && x.AssessmentMonth == today.Month && x.IsActive, ct);
                var openDay = schedule?.OpenDay ?? 25;
                if (today.Day < openDay) continue;
                var people = await db.Persons.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == tenantId && x.IsActive && x.IdentityUserId != null && x.Staff != null && x.Staff.Vacancy != null)
                    .Select(x => new PersonRow(x.PersonId, x.IdentityUserId!, x.Staff!.Vacancy!.OrganizationId, x.Staff.Vacancy.DesignationNav != null ? x.Staff.Vacancy.DesignationNav.Name : x.Staff.Vacancy.JobTitle)).ToListAsync(ct);

                foreach (var assessor in people.Where(x => Rank(x.JobTitle) > 100))
                {
                    var nodeIds = Descendants(assessor.OrganizationId, children);
                    var lower = people.Where(x => x.PersonId != assessor.PersonId && nodeIds.Contains(x.OrganizationId) && Rank(x.JobTitle) > 0 && Rank(x.JobTitle) < Rank(assessor.JobTitle)).ToList();
                    if (lower.Count == 0) continue;
                    var directRank = lower.Max(x => Rank(x.JobTitle));
                    var subjects = lower.Where(x => Rank(x.JobTitle) == directRank).ToList();
                    var existing = await db.StaffAssessments.IgnoreQueryFilters().Where(x => x.TenantId == tenantId && x.AssessorPersonId == assessor.PersonId && x.AssessmentYear == today.Year && x.AssessmentMonth == today.Month).ToListAsync(ct);
                    foreach (var subject in subjects.Where(x => existing.All(y => y.SubjectPersonId != x.PersonId)))
                        db.StaffAssessments.Add(new StaffAssessment { TenantId = tenantId, AssessorPersonId = assessor.PersonId, SubjectPersonId = subject.PersonId, AssessmentYear = today.Year, AssessmentMonth = (byte)today.Month, CreatedDateUtc = DateTime.UtcNow });
                    await db.SaveChangesAsync(ct);

                    var incomplete = await db.StaffAssessments.IgnoreQueryFilters().AnyAsync(x => x.TenantId == tenantId && x.AssessorPersonId == assessor.PersonId && x.AssessmentYear == today.Year && x.AssessmentMonth == today.Month && x.Rating == null, ct);
                    if (!incomplete) continue;
                    var entityId = $"{today:yyyy-MM-dd}:{assessor.PersonId:N}";
                    if (await db.AppNotes.AsNoTracking().AnyAsync(x => x.EntityType == "ASSESSMENT_REMINDER" && x.EntityId == entityId, ct)) continue;
                    db.AppNotes.Add(new AppNote { TenantId = tenantId, Title = "Monthly assessment is pending", NoteBody = $"Please complete your team assessment for {today:MMMM yyyy}.", NoteTypeCode = "NOTIFICATION", SourceTypeCode = "ADMIN", CategoryCode = "ASSESSMENT", PriorityCode = "HIGH", VisibilityTypeCode = "STAFF", MenuCode = "/assessment/mark", ModuleName = "Assessment", EntityType = "ASSESSMENT_REMINDER", EntityId = entityId, StartDateUtc = DateTime.UtcNow, EndDateUtc = DateTime.UtcNow.AddDays(2), IsPublished = true, IsActive = true, AllowDismiss = true, CreatedBy = "SYSTEM", CreatedOnUtc = DateTime.UtcNow, Targets = [new AppNoteTarget { TargetTypeCode = "STAFF", TargetValue = assessor.PersonId.ToString(), IsActive = true }] });
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
    private static int Rank(string? title) { if (string.IsNullOrWhiteSpace(title)) return 0; var v = new string(title.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray()); if (v.Contains("ceo") && !v.Contains("dutyceo")) return 700; if (v.Contains("dutyceo")) return 600; if (v.Contains("manager") && !v.Contains("deputy") && !v.Contains("depty") && !v.Contains("assistant") && !v.Contains("asst")) return 500; if (v.Contains("deputymanager") || v.Contains("deptymanager")) return 400; if (v.Contains("assistantmanager") || v.Contains("asstmanager")) return 300; if (v.Contains("supervisor") || v.Contains("teamlead")) return 200; if (v.Contains("agent") || v.Contains("bellboy")) return 100; return 0; }
    private sealed record PersonRow(Guid PersonId, string IdentityUserId, int OrganizationId, string? JobTitle);
}
