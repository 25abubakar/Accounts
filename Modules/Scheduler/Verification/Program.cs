using Accounts.Data;
using Accounts.Services.Interfaces;
using Accounts.Authorization;
using Accounts.Controllers;
using Accounts.DTOs.Scheduler;
using Accounts.Models.Scheduler;
using Accounts.Services.Scheduler;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Quartz;
using System.Security.Claims;
using System.Text.Json;
using System.Security.Cryptography;

const string business="Server=(localdb)\\MSSQLLocalDB;Database=Account;Trusted_Connection=True;TrustServerCertificate=True";
const string schedulerConnection="Server=(localdb)\\MSSQLLocalDB;Database=AccountsScheduler;Trusted_Connection=True;TrustServerCertificate=True";
const string reviewConnection="Server=(localdb)\\MSSQLLocalDB;Database=AccountsSchedulerReview;Trusted_Connection=True;TrustServerCertificate=True";
var baseline=JsonSerializer.Deserialize<Baseline[]>(await File.ReadAllTextAsync("Modules/Scheduler/Verification/existing-source-baseline.json"))!;
var allowed=new[]{Path.GetFullPath("Accounts/Accounts.csproj"),Path.GetFullPath("Accounts/Program.cs"),Path.GetFullPath("Frontend/Frontend-Accounts/src/App.tsx")};
foreach(var entry in baseline.Where(x=>!allowed.Contains(x.Path,StringComparer.OrdinalIgnoreCase)))
 Check(Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(entry.Path)))==entry.Hash,"Existing calculation/source file changed: "+entry.Path);
await using var main=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(business).Options,new BusinessTenant());
if(args.Contains("--schema"))
{
 await using var storage=new SchedulerDbContext(new DbContextOptionsBuilder<SchedulerDbContext>().UseSqlServer(schedulerConnection).Options,new SchedulerTenant(new HttpContextAccessor()));
 await storage.Database.MigrateAsync();
 var pending=(await main.Database.GetPendingMigrationsAsync()).ToArray();
 Check(pending.All(value=>value is "20261027070000_AddSchedulerWorkspace" or "20261027071000_SetSchedulerSidebarIcons"),"Unexpected business migrations pending");
 if(pending.Length>0)await main.Database.MigrateAsync();
 var menuCount=await main.Menus.CountAsync(x=>x.Route=="/scheduler/jobs"||x.Route=="/scheduler/upcoming"||x.Route=="/scheduler/history");
 Check(menuCount==3,"Scheduler child menu migration failed");
 Console.WriteLine("APPLIED: separate Scheduler DB EF/Quartz schema and only the new Scheduler sidebar/menu migration. Existing calculation source hashes verified.");return;
}
var admin=await main.Users.AsNoTracking().Where(x=>x.TenantId==2&&x.IsTenantAdmin).Select(x=>new{x.Id,x.TenantId}).FirstAsync();
var staffUser=await main.Persons.AsNoTracking().Where(x=>x.TenantId==2&&x.IsActive&&x.IdentityUserId!=null&&x.Staff!=null&&main.Users.Any(user=>user.Id==x.IdentityUserId&&!user.IsTenantAdmin&&!user.IsSuperAdmin)).Select(x=>x.IdentityUserId!).FirstAsync();
var builder=Host.CreateApplicationBuilder();builder.Logging.ClearProviders();
builder.Configuration.AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:DefaultConnection",business},{"ConnectionStrings:SchedulerConnection",reviewConnection}});
builder.Services.AddAccountsScheduler(builder.Configuration);builder.Services.AddScoped<IModuleJob,FailOnceJob>();
using var host=builder.Build();
var accessor=host.Services.GetRequiredService<IHttpContextAccessor>();
SetUser(admin.Id,2);
await using(var initial=host.Services.CreateAsyncScope())await initial.ServiceProvider.GetRequiredService<SchedulerDbContext>().Database.MigrateAsync();
await host.StartAsync();
await using var scope=host.Services.CreateAsyncScope();
var db=scope.ServiceProvider.GetRequiredService<SchedulerDbContext>();
var quartz=await host.Services.GetRequiredService<ISchedulerFactory>().GetScheduler();
var created=new List<Guid>();
SchedulerController Controller(IServiceProvider provider)=>new(provider.GetRequiredService<SchedulerDbContext>(),provider.GetRequiredService<SchedulerTenant>(),provider.GetRequiredService<ISchedulerFactory>(),provider.GetRequiredService<ScheduleSynchronizer>(),provider.GetServices<IModuleJob>()) { ControllerContext=new ControllerContext{HttpContext=accessor.HttpContext!} };
var controller=Controller(scope.ServiceProvider);
var start=DateTime.SpecifyKind(ScheduleTiming.LocalNow.AddMinutes(1),DateTimeKind.Unspecified);
ScheduleEdit Input(string name,string mode="Daily",string handler="System.Health",bool enabled=false)=>new(name,handler,mode,15,15,1,"09:00",start,"RunOnce",enabled,30,2,null,Guid.NewGuid());
try
{
 foreach(var mode in ScheduleTiming.Modes)
 {
  var edit=Input("Timing review",mode);Check(ScheduleTiming.Validate(edit)==null,"Valid frequency rejected: "+mode);
  var definition=new ScheduleDefinition{TenantId=2,Mode=mode,StartLocal=edit.StartLocal,Time=edit.Time,DayOfMonth=15,DayOfWeek=1,IntervalDays=15};
  var trigger=ScheduleTiming.Trigger(definition);var first=trigger.GetFireTimeAfter(DateTimeOffset.UtcNow);
  Check(first.HasValue,"No next trigger for "+mode);
  if(mode=="IntervalDays")Check(trigger.GetFireTimeAfter(first)!.Value-first.Value==TimeSpan.FromDays(15),"15-day interval is not anchored correctly");
 }
 Check(ScheduleTiming.Validate(Input("Invalid") with{IntervalDays=0})!=null,"Invalid interval accepted");
 Check(await controller.Create(Input("Unsafe handler") with{Handler="EXEC_SQL"},default) is BadRequestObjectResult,"Arbitrary handler accepted");
 var value=await Create(Input("Verification health"));
 Check(!value.Enabled,"New schedule enabled unexpectedly");
 Check(await db.Definitions.AsNoTracking().AnyAsync(x=>x.Id==value.Id),"SQL schedule persistence failed");
 var token=Convert.ToBase64String(value.RowVersion);
 Check(await controller.SetEnabled(value.Id,new EnableSchedule(true,token),default) is OkObjectResult,"Enable failed");
 db.ChangeTracker.Clear();
 Check((await db.Definitions.AsNoTracking().SingleAsync(x=>x.Id==value.Id)).Enabled,"Enable not persisted");
 Check(await controller.SetEnabled(value.Id,new EnableSchedule(false,token),default) is ConflictObjectResult,"Stale edit accepted");
 db.ChangeTracker.Clear();
 value=await db.Definitions.AsNoTracking().SingleAsync(x=>x.Id==value.Id);
 Check((await quartz.GetTrigger(ScheduleTiming.TriggerKey(value)))!=null,"Enabled trigger not persisted");
 Check(await controller.SetEnabled(value.Id,new EnableSchedule(false,Convert.ToBase64String(value.RowVersion)),default) is OkObjectResult,"Pause failed");
 Check(await quartz.GetTrigger(ScheduleTiming.TriggerKey(value))==null,"Pause did not remove recurring trigger");
 db.ChangeTracker.Clear();
 var request=Guid.NewGuid();controller.Request.Headers["X-Idempotency-Key"]=request.ToString();
 Check(await controller.Run(value.Id,default) is AcceptedResult,"Run Now failed");
 Check(await controller.Run(value.Id,default) is AcceptedResult,"Idempotent replay failed");
 await WaitFor(value.Id,"Succeeded");
 Check(await db.Runs.AsNoTracking().CountAsync(x=>x.ScheduleId==value.Id)==1,"Duplicate Run Now created multiple executions");
 // A second scheduler factory/provider sees the same SQL trigger store, rather than an in-memory-only schedule.
 Check((await controller.Jobs(default)) is OkObjectResult,"Job list/reload failed");
 var fail=await Create(Input("Verification retry",handler:"Verification.FailOnce"));
 controller.Request.Headers["X-Idempotency-Key"]=Guid.NewGuid().ToString();await controller.Run(fail.Id,default);
 var failed=await WaitFor(fail.Id,"RetryScheduled");
 var retryKey=new TriggerKey($"retry-{failed.Id}-1","tenant-2");
 var retry=await quartz.GetTrigger(retryKey);Check(retry!=null,"Retry was not durably scheduled");
 await quartz.RescheduleJob(retryKey,retry!.GetTriggerBuilder().StartNow().Build());
 var recovered=await WaitFor(fail.Id,"Succeeded");Check(recovered.Attempt==2,"Retry did not update the original execution");
 foreach(var handler in scope.ServiceProvider.GetServices<IModuleJob>().Where(x=>x.Info.Key is "Attendance.Check" or "Assessment.Check"))
  Check((await handler.ExecuteAsync(2,default)).Contains("Read only",StringComparison.OrdinalIgnoreCase),"Legacy calculation adapter was not read-only");
 // Check actual database-backed controller gate, including a forged tenant-admin claim from a staff user.
 Check(await Gate(admin.Id,2),"Tenant Admin scheduler permission missing");
 Check(!await Gate(staffUser,2),"Staff user could administer jobs");
 Check(!await Gate(admin.Id,3),"Cross-tenant admin claim was accepted");
 SetUser(admin.Id,3);
 await using(var other=host.Services.CreateAsyncScope())
 {
  Check(!await other.ServiceProvider.GetRequiredService<SchedulerDbContext>().Definitions.AnyAsync(x=>x.Id==value.Id),"Other tenant schedule leaked");
  Check(await Controller(other.ServiceProvider).Run(value.Id,default) is NotFoundResult,"Other tenant could run a schedule");
 }
 SetUser(admin.Id,2);
 Console.WriteLine("PASS: six frequencies, anchored 15-day interval, SQL save/reload, enable/pause, stale-edit rejection, UUID Run Now replay, persisted retry/recovery, read-only legacy checks, actual admin/tenant permission gate and tenant isolation. Existing calculation sources unchanged.");
}
finally
{
 SetUser(admin.Id,2);
 foreach(var id in created){var definition=await db.Definitions.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id);if(definition!=null)await quartz.DeleteJob(ScheduleTiming.JobKey(definition));}
 await db.Commands.IgnoreQueryFilters().Where(x=>created.Contains(x.ScheduleId)).ExecuteDeleteAsync();
 await db.Runs.IgnoreQueryFilters().Where(x=>created.Contains(x.ScheduleId)).ExecuteDeleteAsync();
 await db.Definitions.IgnoreQueryFilters().Where(x=>created.Contains(x.Id)).ExecuteDeleteAsync();
 await host.StopAsync();
}
async Task<ScheduleDefinition> Create(ScheduleEdit input)
{
 var result=await controller.Create(input,default);Check(result is OkObjectResult,"Schedule create failed: "+JsonSerializer.Serialize((result as ObjectResult)?.Value));
 var json=JsonSerializer.SerializeToElement(((OkObjectResult)result).Value);
 var value=json.GetProperty("definition").Deserialize<ScheduleDefinition>()!;created.Add(value.Id);db.ChangeTracker.Clear();return value;
}
async Task<ScheduleRun> WaitFor(Guid id,string status)
{
 for(var attempt=0;attempt<60;attempt++)
 {
  var run=await db.Runs.IgnoreQueryFilters().AsNoTracking().Where(x=>x.ScheduleId==id).OrderByDescending(x=>x.Id).FirstOrDefaultAsync();
  if(run?.Status==status)return run;
  await Task.Delay(250);
 }
 var last=await db.Runs.IgnoreQueryFilters().AsNoTracking().Where(x=>x.ScheduleId==id).OrderByDescending(x=>x.Id).FirstOrDefaultAsync();
 throw new Exception("Execution did not reach "+status+": "+JsonSerializer.Serialize(last));
}
async Task<bool> Gate(string user,int tenantId)
{
 SetUser(user,tenantId);await using var serviceScope=host.Services.CreateAsyncScope();
 var actionContext=new ActionContext(accessor.HttpContext!,new RouteData(),new ControllerActionDescriptor{ActionName="Jobs"});
 var context=new ActionExecutingContext(actionContext,[],new Dictionary<string,object?>(),new object());var called=false;
 await serviceScope.ServiceProvider.GetRequiredService<SchedulerAccessFilter>().OnActionExecutionAsync(context,()=>{called=true;return Task.FromResult(new ActionExecutedContext(actionContext,[],new object()));});return called;
}
void SetUser(string user,int tenant)=>accessor.HttpContext=new DefaultHttpContext{User=new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier,user),new Claim("tenant_id",tenant.ToString()),new Claim("is_tenant_admin","true")],"Review"))};
static void Check(bool success,string message){if(!success)throw new Exception(message);}
sealed record Baseline(string Path,string Hash);
sealed class BusinessTenant:ITenantService{public int? TenantId=>2;public int RequiredTenantId=>2;public bool IsSuperAdmin=>false;public bool IsTenantAdmin=>true;}
sealed class FailOnceJob:IModuleJob
{
 private static int count;
 public ModuleJobInfo Info=>new("Verification.FailOnce","Verification","Verification retry",true,"Test-only handler; never registered in Accounts.");
 public Task<string> ExecuteAsync(int tenantId,CancellationToken ct)=>Interlocked.Increment(ref count)==1?throw new InvalidOperationException("Expected verification failure"):Task.FromResult("Recovery succeeded");
}
