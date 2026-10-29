using Accounts.Authorization;
using Accounts.Controllers;
using Accounts.Data;
using Accounts.DTOs.Scheduler;
using Accounts.Models.Scheduler;
using Accounts.Services.Interfaces;
using Accounts.Services.Scheduler;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;

const string business="Server=(localdb)\\MSSQLLocalDB;Database=Account;Trusted_Connection=True;TrustServerCertificate=True";
const string review="Server=(localdb)\\MSSQLLocalDB;Database=AccountsSchedulerReview;Trusted_Connection=True;TrustServerCertificate=True";
var builder=Host.CreateApplicationBuilder();builder.Logging.ClearProviders();
builder.Configuration.AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:DefaultConnection",business},{"ConnectionStrings:SchedulerConnection",review}});
builder.Services.AddAccountsScheduler(builder.Configuration);
builder.Services.AddScoped<IModuleJob,FailOnceJob>();builder.Services.AddScoped<IModuleJob,BlockingJob>();
using var host=builder.Build();
Check(!host.Services.GetServices<IHostedService>().Any(x=>x is SchedulerExecutionService),"API registered execution engine");
Check(!typeof(SchedulerController).Assembly.GetReferencedAssemblies().Any(x=>x.Name!.StartsWith("Quartz")),"Quartz dependency remains");
var accessor=host.Services.GetRequiredService<IHttpContextAccessor>();SetTenant(2);
await using var scope=host.Services.CreateAsyncScope();
var db=scope.ServiceProvider.GetRequiredService<SchedulerDbContext>();await db.Database.MigrateAsync();
var queue=host.Services.GetRequiredService<SchedulerQueue>();
var controller=new SchedulerController(db,scope.ServiceProvider.GetRequiredService<SchedulerTenant>(),host.Services.GetRequiredService<ScheduleSynchronizer>(),scope.ServiceProvider.GetServices<IModuleJob>()) {ControllerContext=new ControllerContext{HttpContext=accessor.HttpContext!}};
var created=new List<Guid>();Process? worker=null;
var anchor=new DateTime(2026,1,1,9,0,0,DateTimeKind.Unspecified);
try
{
 foreach(var mode in ScheduleTiming.Modes)
 {
  var value=new ScheduleDefinition{Mode=mode,StartLocal=anchor,Time="09:15",DayOfMonth=15,DayOfWeek=1,IntervalDays=15};
  var next=ScheduleTiming.NextAfter(value,new DateTime(2025,12,31,0,0,0,DateTimeKind.Utc));
  Check(next.HasValue&&next>=ScheduleTiming.StartUtc(value),"Invalid next occurrence: "+mode);
  if(mode=="IntervalDays")Check(ScheduleTiming.NextAfter(value,next.Value)-next.Value==TimeSpan.FromDays(15),"Interval anchor drift");
 }
 var hourly=new ScheduleDefinition{Mode="Hourly",StartLocal=anchor,Time="00:15"};
 Check(ScheduleTiming.NextAfter(hourly,new DateTime(2026,1,1,5,20,0,DateTimeKind.Utc))==new DateTime(2026,1,1,6,15,0,DateTimeKind.Utc),"Hourly minute/timezone mismatch");
 var monthly=new ScheduleDefinition{Mode="Monthly",StartLocal=anchor,Time="00:01",DayOfMonth=31};
 Check(ScheduleTiming.NextAfter(monthly,new DateTime(2026,1,31,0,0,0,DateTimeKind.Utc))==new DateTime(2026,3,30,19,1,0,DateTimeKind.Utc),"Invalid month end handling");
 var once=new ScheduleDefinition{Mode="Once",StartLocal=anchor,MisfirePolicy="Skip"};
 Check(ScheduleTiming.FirstDue(once,DateTime.UtcNow)==null,"Missed Once Skip replayed");
 once.MisfirePolicy="RunOnce";Check(ScheduleTiming.FirstDue(once,DateTime.UtcNow)==ScheduleTiming.StartUtc(once),"Recovery RunOnce lost");

 var definition=await Create("System.Health");
 var command=Guid.NewGuid();accessor.HttpContext!.Request.Headers["X-Idempotency-Key"]=command.ToString();
 Check(await controller.Run(definition.Id,default) is AcceptedResult,"Run Now not queued");
 Check(await controller.Run(definition.Id,default) is AcceptedResult,"Idempotent replay failed");
 Check(!await db.Runs.IgnoreQueryFilters().AnyAsync(x=>x.ScheduleId==definition.Id),"API executed a job without worker");
 await Task.WhenAll(queue.CollectAsync(default),queue.CollectAsync(default));
 var run=await Latest(definition.Id);await Execute(run.Id);await Execute(run.Id);
 run=await Latest(definition.Id);Check(run.Status=="Succeeded"&&run.Attempt==1,"Duplicate execution or execution failure");
 Check(await db.Runs.IgnoreQueryFilters().CountAsync(x=>x.ScheduleId==definition.Id)==1,"Duplicate Run Now occurrence");

 var retry=await Create("Verification.FailOnce");var retryRun=await AddRun(retry,true);
 await Execute(retryRun.Id);retryRun=await Latest(retry.Id);Check(retryRun.Status=="RetryScheduled"&&retryRun.NextAttemptUtc>DateTime.UtcNow,"Retry was not persisted");
 await db.Runs.IgnoreQueryFilters().Where(x=>x.Id==retryRun.Id).ExecuteUpdateAsync(set=>set.SetProperty(x=>x.NextAttemptUtc,DateTime.UtcNow.AddSeconds(-1)));
 await Execute(retryRun.Id);Check((await Latest(retry.Id)).Status=="Succeeded"&&(await Latest(retry.Id)).Attempt==2,"Retry recovery failed");

 var cancelled=await AddRun(definition,false);await Execute(cancelled.Id);Check((await Latest(definition.Id)).Status=="Cancelled","Disabled scheduled job ran");
 var crash=await AddRun(definition,true);await db.Runs.IgnoreQueryFilters().Where(x=>x.Id==crash.Id).ExecuteUpdateAsync(set=>set.SetProperty(x=>x.Status,"Running").SetProperty(x=>x.Attempt,1).SetProperty(x=>x.LeaseUntilUtc,DateTime.UtcNow.AddMinutes(-1)));
 await Execute(crash.Id);Check((await Latest(definition.Id)).Status=="Succeeded"&&(await Latest(definition.Id)).Attempt==2,"Expired crash lease not recovered");

 var blocking=await Create("Verification.Blocking");var first=await AddRun(blocking,true);var second=await AddRun(blocking,true);
 var active=Execute(first.Id);await BlockingJob.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
 await Execute(second.Id);Check((await db.Runs.IgnoreQueryFilters().AsNoTracking().SingleAsync(x=>x.Id==second.Id)).Status=="Queued","Overlapping handler execution");
 BlockingJob.Release.TrySetResult();await active;await Execute(second.Id);
 Check((await Latest(blocking.Id)).Status=="Succeeded","Lock was not released");

 SetTenant(3);controller.ControllerContext=new ControllerContext{HttpContext=accessor.HttpContext!};
 Check(await controller.Run(definition.Id,default) is NotFoundResult,"Cross tenant access");
 SetTenant(2);controller.ControllerContext=new ControllerContext{HttpContext=accessor.HttpContext!};
 // Launch the actual separate executable: it needs no API process or HTTP endpoint.
 var external=await Create("System.Health");var externalKey=Guid.NewGuid();accessor.HttpContext!.Request.Headers["X-Idempotency-Key"]=externalKey.ToString();
 Check(await controller.Run(external.Id,default) is AcceptedResult,"External command queue failed");
 var config=Path.GetFullPath("Modules/Scheduler/Verification/obj/worker-review-config.json");Directory.CreateDirectory(Path.GetDirectoryName(config)!);
 await File.WriteAllTextAsync(config,JsonSerializer.Serialize(new{ConnectionStrings=new{DefaultConnection=business,SchedulerConnection=review},Scheduler=new{PollSeconds=2,Concurrency=2,LocalControlEnabled=false}}));
 var start=new ProcessStartInfo(Path.GetFullPath("_dev/scratch/scheduler-windows-build/Accounts.Scheduler.Worker.exe")){CreateNoWindow=true,UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true};
 start.ArgumentList.Add("--config");start.ArgumentList.Add(config);worker=Process.Start(start)!;
 var output=worker.StandardOutput.ReadToEndAsync();var errors=worker.StandardError.ReadToEndAsync();
 for(var i=0;i<80;i++){if(await db.Runs.IgnoreQueryFilters().AnyAsync(x=>x.ScheduleId==external.Id&&x.Status=="Succeeded"))break;await Task.Delay(250);}
 Check(await db.Runs.IgnoreQueryFilters().AnyAsync(x=>x.ScheduleId==external.Id&&x.Status=="Succeeded"),"Actual Windows Worker console host did not execute the persisted command");
 worker.Kill(true);await worker.WaitForExitAsync();await output;await errors;worker=null;
 var queued=Guid.NewGuid();accessor.HttpContext!.Request.Headers["X-Idempotency-Key"]=queued.ToString();await controller.Run(external.Id,default);
 await Task.Delay(2500);Check(!await db.Runs.IgnoreQueryFilters().AnyAsync(x=>x.ScheduleId==external.Id&&x.Occurrence=="manual-"+queued.ToString("N")),"Job executed after worker stopped");
 worker=Process.Start(start)!;output=worker.StandardOutput.ReadToEndAsync();errors=worker.StandardError.ReadToEndAsync();
 for(var i=0;i<80;i++){if(await db.Runs.IgnoreQueryFilters().AnyAsync(x=>x.ScheduleId==external.Id&&x.Occurrence=="manual-"+queued.ToString("N")&&x.Status=="Succeeded"))break;await Task.Delay(250);}
 Check(await db.Runs.IgnoreQueryFilters().AnyAsync(x=>x.ScheduleId==external.Id&&x.Occurrence=="manual-"+queued.ToString("N")&&x.Status=="Succeeded"),"Worker restart lost the queued command");
 Console.WriteLine("PASS: no Quartz reference or API execution host; six frequencies/Pakistan time/month ends; SQL queue/idempotency; retries; disabled-run cancellation; crash lease recovery; handler overlap lock/release; tenant isolation; real worker executable executes independently, stops execution on exit and recovers queued work after restart.");
}
finally
{
 BlockingJob.Release.TrySetResult();
 if(worker is {HasExited:false}){worker.Kill(true);await worker.WaitForExitAsync();}
 await db.Commands.IgnoreQueryFilters().Where(x=>created.Contains(x.ScheduleId)).ExecuteDeleteAsync();
 await db.Runs.IgnoreQueryFilters().Where(x=>created.Contains(x.ScheduleId)).ExecuteDeleteAsync();
 await db.Definitions.IgnoreQueryFilters().Where(x=>created.Contains(x.Id)).ExecuteDeleteAsync();
}
async Task<ScheduleDefinition> Create(string handler)
{
 var key=Guid.NewGuid();var edit=new ScheduleEdit("Windows worker verification "+key.ToString("N")[..6],handler,"Daily",15,1,1,"09:00",DateTime.SpecifyKind(ScheduleTiming.LocalNow,DateTimeKind.Unspecified),"RunOnce",false,30,2,null,key);
 Check(await controller.Create(edit,default) is OkObjectResult,"API save failed");
 var value=await db.Definitions.AsNoTracking().SingleAsync(x=>x.CreationKey==key);created.Add(value.Id);return value;
}
async Task<ScheduleRun> AddRun(ScheduleDefinition definition,bool manual)
{
 var value=new ScheduleRun{ScheduleId=definition.Id,TenantId=definition.TenantId,Occurrence="verification-"+Guid.NewGuid(),ScheduledUtc=DateTime.UtcNow,Status="Queued",NextAttemptUtc=DateTime.UtcNow,IsManual=manual,ScheduleRevision=definition.Revision};
 db.Runs.Add(value);await db.SaveChangesAsync();return value;
}
async Task Execute(long id){await using var execution=host.Services.CreateAsyncScope();await execution.ServiceProvider.GetRequiredService<ModuleJobDispatcher>().ExecuteAsync(id,default);}
Task<ScheduleRun> Latest(Guid id)=>db.Runs.IgnoreQueryFilters().AsNoTracking().Where(x=>x.ScheduleId==id).OrderByDescending(x=>x.Id).FirstAsync();
void SetTenant(int tenant)=>accessor.HttpContext=new DefaultHttpContext{User=new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier,"verification"),new Claim("tenant_id",tenant.ToString()),new Claim("is_tenant_admin","true")],"Review"))};
static void Check(bool valid,string message){if(!valid)throw new Exception(message);}
sealed class FailOnceJob:IModuleJob
{
 private static int count;
 public ModuleJobInfo Info=>new("Verification.FailOnce","Verification","Fail once",true,"Test-only.");
 public Task<string> ExecuteAsync(int tenantId,CancellationToken ct)=>Interlocked.Increment(ref count)==1?throw new InvalidOperationException("Expected verification failure"):Task.FromResult("Recovered");
}
sealed class BlockingJob:IModuleJob
{
 public static TaskCompletionSource Started=new(TaskCreationOptions.RunContinuationsAsynchronously),Release=new(TaskCreationOptions.RunContinuationsAsynchronously);
 public ModuleJobInfo Info=>new("Verification.Blocking","Verification","Blocking",true,"Test-only.");
 public async Task<string> ExecuteAsync(int tenantId,CancellationToken ct){Started.TrySetResult();await Release.Task.WaitAsync(ct);return "Completed";}
}
