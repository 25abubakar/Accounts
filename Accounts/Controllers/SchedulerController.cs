using Accounts.Authorization;
using Accounts.Data;
using Accounts.DTOs.Scheduler;
using Accounts.Models.Scheduler;
using Accounts.Services.Interfaces;
using Accounts.Services.Scheduler;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Controllers;

[ApiController,Route("api/scheduler"),Authorize(Policy="SchedulerAdmin"),ServiceFilter(typeof(SchedulerAccessFilter))]
public sealed class SchedulerController(SchedulerDbContext db,SchedulerTenant tenant,
    ScheduleSynchronizer synchronizer,IEnumerable<IModuleJob> handlers) : ControllerBase
{
    [HttpGet("session")]
    public IActionResult Session()=>Ok(new {tenantId=tenant.Id,legacyTimersUnchanged=true,executionHost="WindowsService"});
    [HttpGet("csrf")]
    public IActionResult Csrf([FromServices] IAntiforgery antiforgery)=>Ok(new{token=antiforgery.GetAndStoreTokens(HttpContext).RequestToken});
    [HttpGet("catalog")]
    public IActionResult Catalog()=>Ok(handlers.Select(x=>x.Info).Where(x=>!x.SystemOnly));
    [HttpGet("jobs")]
    public async Task<IActionResult> Jobs(CancellationToken ct)
    {
        var values=await db.Definitions.AsNoTracking().OrderBy(x=>x.Name).ToListAsync(ct);
        var result=new List<object>();
        foreach(var value in values)
        {
            var last=await db.Runs.AsNoTracking().Where(x=>x.ScheduleId==value.Id).OrderByDescending(x=>x.StartedUtc).FirstOrDefaultAsync(ct);
            result.Add(new {definition=value,nextRunUtc=value.Enabled?value.NextRunUtc:null,lastRun=last,
                syncPending=value.RuntimeRevision!=value.Revision});
        }
        return Ok(result);
    }
    [HttpGet("upcoming")]
    public async Task<IActionResult> Upcoming(CancellationToken ct)
    {
        var result=new List<object>();
        foreach(var value in await db.Definitions.AsNoTracking().Where(x=>x.Enabled&&x.NextRunUtc!=null).ToListAsync(ct))
            result.Add(new{scheduleId=value.Id,name=value.Name,value.Handler,nextRunUtc=value.NextRunUtc,trigger="Scheduled"});
        foreach(var run in await db.Runs.AsNoTracking().Where(x=>(x.Status=="Queued"||x.Status=="RetryScheduled")&&x.NextAttemptUtc!=null).ToListAsync(ct))
        {
            var definition=await db.Definitions.AsNoTracking().SingleAsync(x=>x.Id==run.ScheduleId,ct);
            result.Add(new{scheduleId=definition.Id,name=definition.Name,definition.Handler,nextRunUtc=run.NextAttemptUtc,trigger=run.Status+" #"+run.Id});
        }
        return Ok(result);
    }
    [HttpGet("runs")]
    public async Task<IActionResult> Runs([FromQuery] Guid? scheduleId,CancellationToken ct)
    {
        var query=db.Runs.AsNoTracking();if(scheduleId.HasValue)query=query.Where(x=>x.ScheduleId==scheduleId);
        return Ok(await query.OrderByDescending(x=>x.Id).Take(500).Select(x=>new{
            x.Id,x.ScheduleId,name=db.Definitions.Where(d=>d.Id==x.ScheduleId).Select(d=>d.Name).FirstOrDefault(),
            x.ScheduledUtc,x.StartedUtc,x.FinishedUtc,x.Status,x.Attempt,x.Result,x.Error
        }).ToListAsync(ct));
    }
    [HttpPost("preview")]
    public async Task<IActionResult> Preview(ScheduleEdit input,[FromServices] SchedulerCalendar calendar,[FromQuery] Guid? scheduleId,CancellationToken ct)
    {
        var error=ScheduleTiming.Validate(input);if(error!=null)return BadRequest(new{message=error});
        var value=new ScheduleDefinition {TenantId=tenant.Id};
        if(scheduleId.HasValue)
        {
            var saved=await db.Definitions.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==scheduleId,ct);
            if(saved==null)return NotFound();
            if(saved.Handler==input.Handler)value.ManagedKey=saved.ManagedKey;
        }
        Apply(value,input);
        var times=new List<DateTime>();var next=await calendar.NextAfterAsync(value,DateTime.UtcNow,ct);
        while(next.HasValue&&times.Count<5){times.Add(next.Value);next=await calendar.NextAfterAsync(value,next.Value,ct);}
        return Ok(times);
    }
    [HttpPost("jobs")]
    public async Task<IActionResult> Create(ScheduleEdit input,CancellationToken ct)
    {
        var error=Validate(input);if(error!=null)return BadRequest(new{message=error});
        if(await db.Definitions.AnyAsync(x=>x.CreationKey==input.CreationKey,ct))
            return Conflict(new{message="This create request was already saved. Refresh the list before trying again."});
        var value=new ScheduleDefinition {TenantId=tenant.Id};Apply(value,input);value.UpdatedBy=tenant.Actor;
        db.Definitions.Add(value);await db.SaveChangesAsync(ct);
        return await Saved(value,ct);
    }
    [HttpPut("jobs/{id:guid}")]
    public async Task<IActionResult> Update(Guid id,ScheduleEdit input,CancellationToken ct)
    {
        var error=Validate(input);if(error!=null)return BadRequest(new{message=error});
        var value=await db.Definitions.SingleOrDefaultAsync(x=>x.Id==id,ct);if(value==null)return NotFound();
        if(!Version(value,input.RowVersion))return BadRequest(new{message="A valid row version is required."});
        Apply(value,input);value.Revision++;value.UpdatedBy=tenant.Actor;value.UpdatedUtc=DateTime.UtcNow;
        try { await db.SaveChangesAsync(ct); }
        catch(DbUpdateConcurrencyException){return Conflict(new{message="This schedule changed. Refresh and edit its latest version."});}
        return await Saved(value,ct);
    }
    [HttpPost("jobs/{id:guid}/enabled")]
    public async Task<IActionResult> SetEnabled(Guid id,EnableSchedule input,CancellationToken ct)
    {
        var value=await db.Definitions.SingleOrDefaultAsync(x=>x.Id==id,ct);if(value==null)return NotFound();
        if(!Version(value,input.RowVersion))return BadRequest(new{message="A valid row version is required."});
        value.Enabled=input.Enabled;value.Revision++;value.UpdatedBy=tenant.Actor;value.UpdatedUtc=DateTime.UtcNow;
        try{await db.SaveChangesAsync(ct);}catch(DbUpdateConcurrencyException){return Conflict(new{message="This schedule changed. Refresh the list."});}
        return await Saved(value,ct);
    }
    [HttpPost("jobs/{id:guid}/run")]
    public async Task<IActionResult> Run(Guid id,CancellationToken ct)
    {
        var value=await db.Definitions.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id,ct);if(value==null)return NotFound();
        if(!Guid.TryParse(Request.Headers["X-Idempotency-Key"],out var key))return BadRequest(new{message="A UUID idempotency key is required."});
        var existing=await db.Commands.SingleOrDefaultAsync(x=>x.Id==key,ct);
        if(existing!=null)return existing.ScheduleId==id?Accepted(new{queued=true,replayed=true}):Conflict(new{message="Request key belongs to a different job."});
        var command=new SchedulerCommand {Id=key,ScheduleId=id,TenantId=tenant.Id};db.Commands.Add(command);
        try { await db.SaveChangesAsync(ct); }
        catch(DbUpdateException){return Conflict(new{message="Request is already queued. Refresh its run history."});}
        // SQL outbox: the Windows Service owns dispatch. API availability cannot execute jobs.
        return Accepted(new{queued=true});
    }
    private string? Validate(ScheduleEdit input)=>ScheduleTiming.Validate(input)??
        (handlers.Any(x=>x.Info.Key==input.Handler&&!x.Info.SystemOnly)?null:"Choose a registered tenant module job.");
    private bool Version(ScheduleDefinition value,string? version)
    {
        try {var bytes=Convert.FromBase64String(version??"");if(bytes.Length!=8)return false;db.Entry(value).Property(x=>x.RowVersion).OriginalValue=bytes;return true;}
        catch(FormatException){return false;}
    }
    private async Task<IActionResult> Saved(ScheduleDefinition value,CancellationToken ct)
    {
        await synchronizer.ApplyAsync(value.Id,ct);
        await db.Entry(value).ReloadAsync(ct);
        return Ok(new{definition=value,syncPending=value.RuntimeRevision!=value.Revision});
    }
    private static void Apply(ScheduleDefinition value,ScheduleEdit input)
    {
        if(value.Handler.Length>0&&value.Handler!=input.Handler)value.ManagedKey=null;
        value.Name=input.Name.Trim();value.Handler=input.Handler;value.Mode=input.Mode;value.IntervalDays=input.IntervalDays;
        value.DayOfMonth=input.DayOfMonth;value.DayOfWeek=input.DayOfWeek;value.Time=input.Time;value.StartLocal=input.StartLocal;
        value.MisfirePolicy=input.MisfirePolicy;value.Enabled=input.Enabled;value.TimeoutSeconds=input.TimeoutSeconds;
        value.RetryCount=input.RetryCount;if(value.CreationKey==Guid.Empty)value.CreationKey=input.CreationKey;
        value.IntervalMinutes=input.IntervalMinutes;
    }
}
public sealed record EnableSchedule(bool Enabled,string RowVersion);
