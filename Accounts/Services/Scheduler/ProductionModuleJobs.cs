using Accounts.Services.Scheduler.Jobs;
using Accounts.Idempotency;
using Accounts.Models.Scheduler;
using Accounts.Services.Interfaces;
using Accounts.Services.Services;
using Accounts.Services.Fax;

namespace Accounts.Services.Scheduler;

public sealed class AttendanceFinalizationJob(IServiceProvider services) : IModuleJob
{
    public ModuleJobInfo Info=>new("Attendance.Finalize","Attendance","Finalize attendance",false,
        "Applies the tenant's saved schedules and approved attendance rules; finalizes the current period.");
    public async Task<string> ExecuteAsync(int tenantId,CancellationToken ct)
        =>$"Attendance: {await services.GetRequiredService<AttendanceFinalizationJobService>().RunTenantAsync(tenantId,ct)} rows updated.";
}
public sealed class AssessmentGenerationJob(IServiceProvider services) : IModuleJob
{
    public ModuleJobInfo Info=>new("Assessment.Generate","Assessment","Monthly assessment generation",false,
        "Creates missing assessments in the active rule-defined cycle; preserves saved ratings and amounts.",ConcurrencyKey:"Assessment.Cycle");
    public async Task<string> ExecuteAsync(int tenantId,CancellationToken ct)
    {
        var db=services.GetRequiredService<Accounts.Data.ApplicationDbContext>();
        var before=await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(db.StaffAssessments.Where(x=>x.TenantId==tenantId),ct);
        await services.GetRequiredService<AssessmentCycleJobService>().RunTenantAsync(tenantId,true,true,ct);
        var after=await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(db.StaffAssessments.Where(x=>x.TenantId==tenantId),ct);
        return $"Monthly assessment cycle processed: {after-before} new entries; existing ratings and amounts preserved.";
    }
}
public sealed class AssessmentRemindersJob(IServiceProvider services) : IModuleJob
{
    public ModuleJobInfo Info=>new("Assessment.Reminders","Assessment","Pending assessment reminders",false,
        "Refreshes reminders during the active cycle; does not generate or score assessments.",ConcurrencyKey:"Assessment.Cycle");
    public async Task<string> ExecuteAsync(int tenantId,CancellationToken ct)
    {await services.GetRequiredService<AssessmentCycleJobService>().RunTenantAsync(tenantId,false,true,ct);return "Assessment reminders refreshed.";}
}
public sealed class ProcessAutoTransferJob(IServiceProvider services) : IModuleJob
{
    public ModuleJobInfo Info=>new("Process.AutoTransfer","Process","Overdue report auto-transfer",false,
        "Runs the existing timeout/alternative-reporter procedure. System maintenance only.",SystemOnly:true);
    public async Task<string> ExecuteAsync(int tenantId,CancellationToken ct)
    {await services.GetRequiredService<ProcessAutoTransferJobService>().RunAsync(ct);return "Overdue process-report transfer completed.";}
}
public sealed class ChatExpiryJob(IServiceProvider services) : IModuleJob
{
    public ModuleJobInfo Info=>new("Chat.ViewOnceCleanup","Chat","View-once attachment expiry",false,
        "Expires unopened view-once attachments using saved tenant chat rules. System maintenance only.",SystemOnly:true);
    public async Task<string> ExecuteAsync(int tenantId,CancellationToken ct)
    {await services.GetRequiredService<ChatViewOnceExpiryJobService>().RunAsync(ct);return "View-once cleanup completed.";}
}
public sealed class ConversionCleanupJob(IServiceProvider services) : IModuleJob
{
    public ModuleJobInfo Info=>new("Files.ConversionCleanup","Library","Temporary conversion cleanup",false,
        "Cleans only stale conversion directories under the configured Accounts application root. System maintenance only.",SystemOnly:true);
    public async Task<string> ExecuteAsync(int tenantId,CancellationToken ct)
    {await services.GetRequiredService<ConversionFileCleanupJobService>().RunAsync(ct);return "Conversion cleanup completed.";}
}
public sealed class IdempotencyExpiryJob(IServiceProvider services) : IModuleJob
{
    public ModuleJobInfo Info=>new("Infrastructure.IdempotencyCleanup","Infrastructure","Expired idempotency cleanup",false,
        "Deletes expired request replay records. System maintenance only.",SystemOnly:true);
    public async Task<string> ExecuteAsync(int tenantId,CancellationToken ct)
        =>$"Expired idempotency records deleted: {await services.GetRequiredService<IdempotencyExpiryJobService>().RunAsync(ct)}.";
}
public sealed class FaxReconciliationJob(IServiceProvider services) : IModuleJob
{
    public ModuleJobInfo Info=>new("Fax.Reconcile","Fax","Reconcile outbound fax status",false,
        "Reconciles stale provider states and missed callbacks. It never resends document content.",ConcurrencyKey:"Fax.Reconcile");
    public async Task<string> ExecuteAsync(int tenantId,CancellationToken ct)
        =>$"Fax transmissions reconciled: {await services.GetRequiredService<IFaxDispatchService>().ReconcilePendingAsync(tenantId,ct)}.";
}
