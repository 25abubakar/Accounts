using Accounts.Authorization;
using Accounts.DTOs.Scheduler;
using Accounts.Services.Interfaces;
using Accounts.Services.Scheduler;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Accounts.Controllers;

[ApiController,Route("api/scheduler"),Authorize,ServiceFilter(typeof(SchedulerAccessFilter))]
public sealed class SchedulerController(ISchedulerService service) : ControllerBase
{
    [HttpGet("session")] public Task<IActionResult> Session(CancellationToken ct)=>Reply(()=>service.SessionAsync(ct));
    [HttpGet("catalog")] public Task<IActionResult> Catalog(CancellationToken ct)=>Reply(()=>service.CatalogAsync(ct));
    [HttpGet("jobs")] public Task<IActionResult> Jobs(CancellationToken ct)=>Reply(()=>service.JobsAsync(ct));
    [HttpGet("upcoming")] public Task<IActionResult> Upcoming(CancellationToken ct)=>Reply(()=>service.UpcomingAsync(ct));
    [HttpGet("runs")] public Task<IActionResult> Runs([FromQuery] Guid? scheduleId,CancellationToken ct)=>Reply(()=>service.RunsAsync(scheduleId,ct));
    [HttpPost("preview")] public Task<IActionResult> Preview(ScheduleEdit input,[FromQuery] Guid? scheduleId,CancellationToken ct)=>Reply(()=>service.PreviewAsync(input,scheduleId,ct));
    [HttpPost("jobs")] public Task<IActionResult> Create(ScheduleEdit input,CancellationToken ct)=>Reply(()=>service.SaveAsync(null,input,ct));
    [HttpPut("jobs/{id:guid}")] public Task<IActionResult> Update(Guid id,ScheduleEdit input,CancellationToken ct)=>Reply(()=>service.SaveAsync(id,input,ct));
    [HttpDelete("jobs/{id:guid}")] public Task<IActionResult> Delete(Guid id,[FromQuery] string rowVersion,CancellationToken ct)=>Reply(()=>service.DeleteAsync(id,rowVersion,ct));
    [HttpPost("jobs/{id:guid}/enabled")] public Task<IActionResult> SetEnabled(Guid id,EnableSchedule input,CancellationToken ct)=>Reply(()=>service.EnableAsync(id,input,ct));
    [HttpPost("jobs/{id:guid}/run")] public Task<IActionResult> Run(Guid id,CancellationToken ct)=>Reply(()=>service.RunAsync(id,Request.Headers["X-Idempotency-Key"],ct),StatusCodes.Status202Accepted);
    [HttpGet("csrf")] public IActionResult Csrf([FromServices] IAntiforgery antiforgery)=>Ok(new{token=antiforgery.GetAndStoreTokens(HttpContext).RequestToken});
    private async Task<IActionResult> Reply<T>(Func<Task<T>> action,int status=StatusCodes.Status200OK)
    {
        try{return StatusCode(status,await action());}
        catch(SchedulerFault error){return StatusCode(error.Status,new{message=error.Message});}
    }
}
