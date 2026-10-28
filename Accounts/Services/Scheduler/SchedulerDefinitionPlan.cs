using Accounts.Data;
using Accounts.Models.Scheduler;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace Accounts.Services.Scheduler;

public sealed class SchedulerDefinitionPlan(SchedulerDbContext db,SchedulerStorage storage,ScheduleSynchronizer synchronizer,IConfiguration configuration)
{
    public async Task<int> InstallAsync(CancellationToken ct,bool enableNew=true)
    {
        await using var connection=new SqlConnection(storage.BusinessConnection);await connection.OpenAsync(ct);
        var tenants=new List<(int Id,int? OpenDay)>();
        await using(var command=connection.CreateCommand())
        {
            command.CommandText="""
                SELECT tenant.Id,(SELECT TOP(1) savedRule.OpenDay FROM dbo.AssessmentBonusRules savedRule WHERE savedRule.TenantId=tenant.Id AND savedRule.IsActive=1 ORDER BY savedRule.Id)
                FROM dbo.Tenants tenant WHERE tenant.IsActive=1 AND EXISTS(SELECT 1 FROM dbo.Persons person WHERE person.TenantId=tenant.Id AND person.IsActive=1)
                """;
            await using var reader=await command.ExecuteReaderAsync(ct);
            while(await reader.ReadAsync(ct))tenants.Add((reader.GetInt32(0),reader.IsDBNull(1)?null:Convert.ToInt32(reader.GetValue(1))));
        }
        var added=0;
        foreach(var tenant in tenants)
        {
            added+=await EnsureAsync(tenant.Id,"attendance-hourly","Attendance finalization","Attendance.Finalize","Hourly",0,1,ct,enabled:enableNew);
            if(tenant.OpenDay.HasValue)
            {
                added+=await EnsureAsync(tenant.Id,"assessment-monthly","Monthly assessment generation","Assessment.Generate","Monthly",0,tenant.OpenDay.Value,ct,enabled:enableNew);
                added+=await EnsureAsync(tenant.Id,"assessment-reminders","Daily assessment reminders","Assessment.Reminders","Daily",0,1,ct,"09:00",enableNew);
            }
        }
        added+=await EnsureAsync(0,"process-overdue","Overdue report auto-transfer","Process.AutoTransfer","IntervalMinutes",5,1,ct,enabled:enableNew);
        added+=await EnsureAsync(0,"chat-view-once","View-once attachment expiry","Chat.ViewOnceCleanup","Hourly",0,1,ct,enabled:enableNew);
        added+=await EnsureAsync(0,"conversion-cleanup","Temporary conversion cleanup","Files.ConversionCleanup","IntervalMinutes",15,1,ct,enabled:enableNew);
        added+=await EnsureAsync(0,"idempotency-cleanup","Expired idempotency cleanup","Infrastructure.IdempotencyCleanup","IntervalMinutes",
            Math.Clamp(configuration.GetValue("Idempotency:CleanupIntervalMinutes",30),1,1440),1,ct,enabled:enableNew);
        return added;
    }

    private async Task<int> EnsureAsync(int tenantId,string key,string name,string handler,string mode,int minutes,int day,CancellationToken ct,string time="00:00",bool enabled=true)
    {
        if(await db.Definitions.IgnoreQueryFilters().AnyAsync(x=>x.TenantId==tenantId&&x.ManagedKey==key,ct))return 0;
        var local=ScheduleTiming.LocalNow;
        var value=new ScheduleDefinition{TenantId=tenantId,ManagedKey=key,Name=name,Handler=handler,Mode=mode,
            IntervalMinutes=Math.Max(1,minutes),DayOfMonth=day,Time=time,
            StartLocal=DateTime.SpecifyKind(mode=="Monthly"?new DateTime(local.Year,local.Month,1):local.Date,DateTimeKind.Unspecified),
            Enabled=enabled,MisfirePolicy="RunOnce",TimeoutSeconds=600,RetryCount=2,UpdatedBy=enabled?"SYSTEM: scheduler timer migration":"SYSTEM: staged timer migration",
            CreationKey=new Guid(SHA256.HashData(Encoding.UTF8.GetBytes($"AccountsScheduler:{tenantId}:{key}"))[..16])};
        db.Definitions.Add(value);await db.SaveChangesAsync(ct);await synchronizer.ApplyAsync(value.Id,ct);
        return 1;
    }

    public async Task<int> ActivateStagedAsync(CancellationToken ct)
    {
        var rows=await db.Definitions.IgnoreQueryFilters().Where(x=>x.ManagedKey!=null&&x.UpdatedBy=="SYSTEM: staged timer migration"&&!x.Enabled).ToListAsync(ct);
        foreach(var row in rows){row.Enabled=true;row.Revision++;row.UpdatedUtc=DateTime.UtcNow;row.UpdatedBy="SYSTEM: scheduler timer migration";}
        await db.SaveChangesAsync(ct);
        foreach(var row in rows)await synchronizer.ApplyAsync(row.Id,ct);
        return rows.Count;
    }

    public async Task<bool> QueueAssessmentAsync(int tenantId,CancellationToken ct)
    {
        var definition=await db.Definitions.IgnoreQueryFilters().SingleOrDefaultAsync(x=>x.TenantId==tenantId&&x.ManagedKey=="assessment-monthly",ct);
        if(definition==null)return false;
        // Re-resolve the active rule/one-month override without changing an individually paused job.
        definition.Revision++;definition.UpdatedUtc=DateTime.UtcNow;await db.SaveChangesAsync(ct);
        await synchronizer.ApplyAsync(definition.Id,ct);
        if(!definition.Enabled)return false;
        db.Commands.Add(new SchedulerCommand{Id=Guid.NewGuid(),TenantId=tenantId,ScheduleId=definition.Id});
        await db.SaveChangesAsync(ct);return true;
    }

    public async Task RuleChangedAsync(int tenantId,CancellationToken ct)
    {
        var definition=await db.Definitions.IgnoreQueryFilters().SingleOrDefaultAsync(x=>x.TenantId==tenantId&&x.ManagedKey=="assessment-monthly",ct);
        if(definition==null)return;
        await using var connection=new SqlConnection(storage.BusinessConnection);await connection.OpenAsync(ct);
        await using var command=connection.CreateCommand();command.CommandText="SELECT TOP(1) OpenDay FROM dbo.AssessmentBonusRules WHERE TenantId=@tenant ORDER BY Id";
        command.Parameters.AddWithValue("@tenant",tenantId);var day=await command.ExecuteScalarAsync(ct);
        if(day!=null&&day!=DBNull.Value)definition.DayOfMonth=Convert.ToInt32(day);
        definition.Revision++;definition.UpdatedUtc=DateTime.UtcNow;await db.SaveChangesAsync(ct);
        await synchronizer.ApplyAsync(definition.Id,ct);
    }

    public async Task<int> RemoveLegacyTestJobsAsync(CancellationToken ct)
    {
        var ids=await db.Definitions.IgnoreQueryFilters().Where(x=>x.ManagedKey==null&&
            (x.Handler=="Attendance.Check"||x.Handler=="Assessment.Check"||x.Handler=="System.Health"))
            .Select(x=>x.Id).ToArrayAsync(ct);
        await using var transaction=await db.Database.BeginTransactionAsync(ct);
        await db.Commands.IgnoreQueryFilters().Where(x=>ids.Contains(x.ScheduleId)).ExecuteDeleteAsync(ct);
        await db.Runs.IgnoreQueryFilters().Where(x=>ids.Contains(x.ScheduleId)).ExecuteDeleteAsync(ct);
        var deleted=await db.Definitions.IgnoreQueryFilters().Where(x=>ids.Contains(x.Id)).ExecuteDeleteAsync(ct);
        await transaction.CommitAsync(ct);return deleted;
    }
}
