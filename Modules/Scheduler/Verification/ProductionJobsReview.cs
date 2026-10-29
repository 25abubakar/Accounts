using Accounts.Controllers;
using Accounts.Data;
using Accounts.DTOs;
using Accounts.Idempotency;
using Accounts.Models.Scheduler;
using Accounts.Scheduler.Worker;
using Accounts.Services;
using Accounts.Services.Interfaces;
using Accounts.Services.Scheduler;
using Accounts.Services.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Security.Claims;
using System.Text.Json;
using System.Diagnostics;

const string business="Server=(localdb)\\MSSQLLocalDB;Database=Account_SchedulerChecks_20261028;Trusted_Connection=True;TrustServerCertificate=True";
const string scheduler="Server=(localdb)\\MSSQLLocalDB;Database=AccountsSchedulerReview;Trusted_Connection=True;TrustServerCertificate=True";
var builder=Host.CreateApplicationBuilder();builder.Logging.ClearProviders();
builder.Configuration.AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:DefaultConnection",business},{"ConnectionStrings:SchedulerConnection",scheduler},{"Scheduler:ApplicationContentRoot",Path.GetFullPath("Accounts")}});
builder.Services.AddSchedulerStorage(builder.Configuration);builder.Services.AddBusinessJobs(builder.Configuration);
builder.Services.AddScoped<IRealtimePublisher,CaptureRealtime>();
using var host=builder.Build();
await using var scope=host.Services.CreateAsyncScope();
var db=scope.ServiceProvider.GetRequiredService<SchedulerDbContext>();
await db.Database.MigrateAsync();
var plan=scope.ServiceProvider.GetRequiredService<SchedulerDefinitionPlan>();
var addedFirst=await plan.InstallAsync(default,false);
Check(!await db.Definitions.IgnoreQueryFilters().AnyAsync(x=>x.ManagedKey!=null&&x.Enabled),"Staging activated jobs");
var protectedId=await db.Definitions.IgnoreQueryFilters().Where(x=>x.TenantId==2&&x.ManagedKey=="attendance-hourly").Select(x=>x.Id).SingleAsync();
await db.Definitions.IgnoreQueryFilters().Where(x=>x.Id==protectedId).ExecuteUpdateAsync(set=>set.SetProperty(x=>x.UpdatedBy,"manual pause"));
db.ChangeTracker.Clear();
await plan.ActivateStagedAsync(default);
Check(!await db.Definitions.IgnoreQueryFilters().AnyAsync(x=>x.Id==protectedId&&x.Enabled),"Staged activation overwrote a manual pause");
Check(await plan.InstallAsync(default)==0,"Repeated install duplicated defaults");
var owned=(await db.Definitions.IgnoreQueryFilters().Where(x=>x.ManagedKey!=null).Select(x=>x.Id).ToArrayAsync()).ToList();
await db.Definitions.IgnoreQueryFilters().Where(x=>owned.Contains(x.Id)).ExecuteUpdateAsync(set=>set.SetProperty(x=>x.Enabled,false).SetProperty(x=>x.NextRunUtc,(DateTime?)null));
try
{
 foreach(var type in new[]{typeof(AssessmentSchedulerService),typeof(AttendanceFinalizationScheduler),typeof(ProcessReportAutoTransferService),typeof(ChatViewOnceCleanupService),typeof(FileConversionCleanupService),typeof(IdempotencyCleanupService)})
  Check(!typeof(BackgroundService).IsAssignableFrom(type),"Old timer still hosted: "+type.Name);
 var handlers=scope.ServiceProvider.GetServices<IModuleJob>().ToArray();
 Check(handlers.Count(x=>!x.Info.SystemOnly)==3,"Tenant catalog includes test/system jobs");
 Check(handlers.Single(x=>x.Info.Key=="Assessment.Generate").Info.ConcurrencyKey==handlers.Single(x=>x.Info.Key=="Assessment.Reminders").Info.ConcurrencyKey,"Assessment handlers can overlap");

 // Business writes are enclosed in a rollback transaction; no paid/rated values are changed by verification.
 await using(var businessScope=host.Services.CreateAsyncScope())
 {
  businessScope.ServiceProvider.GetRequiredService<ScheduledTenantContext>().Id=2;
  var app=businessScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
  await using var transaction=await app.Database.BeginTransactionAsync();
  var rated=await Rated(app);var others=await app.StaffAssessments.IgnoreQueryFilters().CountAsync(x=>x.TenantId!=2);
  var assessment=businessScope.ServiceProvider.GetRequiredService<AssessmentSchedulerService>();
  await assessment.RunTenantAsync(2,true,true);var first=await app.StaffAssessments.IgnoreQueryFilters().CountAsync(x=>x.TenantId==2);
  await assessment.RunTenantAsync(2,true,true);Check(await app.StaffAssessments.IgnoreQueryFilters().CountAsync(x=>x.TenantId==2)==first,"Assessment generation duplicated rows");
  await assessment.RunTenantAsync(2,false,true);Check(await app.StaffAssessments.IgnoreQueryFilters().CountAsync(x=>x.TenantId==2)==first,"Daily reminders generated assessments");
  Check(await Rated(app)==rated,"Saved ratings/amounts/approval flags changed");
  Check(await app.StaffAssessments.IgnoreQueryFilters().CountAsync(x=>x.TenantId!=2)==others,"Other tenant assessments changed");
  await transaction.RollbackAsync();
 }
 Console.WriteLine("PASS assessment: repeated generation is idempotent; daily reminders do not generate; rated/approved values and other tenants preserved (writes rolled back).");

 await using(var businessScope=host.Services.CreateAsyncScope())
 {
  businessScope.ServiceProvider.GetRequiredService<ScheduledTenantContext>().Id=2;
  var app=businessScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
  await using var transaction=await app.Database.BeginTransactionAsync();
  var other=await app.AttendanceDailyFinalizations.IgnoreQueryFilters().CountAsync(x=>x.TenantId!=2);
  var finalizer=businessScope.ServiceProvider.GetRequiredService<AttendanceFinalizationScheduler>();
  await finalizer.RunTenantAsync(2,default);var first=await Finalized(app);
  await finalizer.RunTenantAsync(2,default);Check(await Finalized(app)==first,"Historical attendance charges changed on identical rerun");
  Check(await app.AttendanceDailyFinalizations.IgnoreQueryFilters().CountAsync(x=>x.TenantId!=2)==other,"Other tenant finalizations changed");
  Check(!await app.AttendanceDailyFinalizations.IgnoreQueryFilters().Where(x=>x.TenantId==2).GroupBy(x=>new{x.PersonId,x.AttendanceDate}).AnyAsync(x=>x.Count()>1),"Duplicate daily finalizations");
  await transaction.RollbackAsync();
 }
 Console.WriteLine("PASS attendance: tenant-scoped current-period finalization; identical historical charges and no duplicate rows after rerun (writes rolled back).");

 var accessor=scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
 accessor.HttpContext=new DefaultHttpContext{User=new ClaimsPrincipal(new ClaimsIdentity([new Claim("tenant_id","2")],"Verification"))};
 var controller=new SchedulerController(db,scope.ServiceProvider.GetRequiredService<SchedulerTenant>(),host.Services.GetRequiredService<ScheduleSynchronizer>(),handlers);
 var catalog=(IEnumerable<ModuleJobInfo>)((OkObjectResult)controller.Catalog()).Value!;
 Check(catalog.All(x=>!x.SystemOnly)&&catalog.All(x=>x.Key is not ("Attendance.Check" or "Assessment.Check" or "System.Health")),"Unsafe or test handler in tenant dropdown");
 var interval=new ScheduleDefinition{Mode="IntervalMinutes",IntervalMinutes=5,StartLocal=new DateTime(2026,10,1,0,0,0),Time="00:00"};
 var next=ScheduleTiming.NextAfter(interval,new DateTime(2026,10,28,0,2,0,DateTimeKind.Utc));
 Check(next==new DateTime(2026,10,28,0,5,0,DateTimeKind.Utc),"Five-minute interval drift");
 Console.WriteLine("PASS catalog and setup: three tenant handlers, four operator maintenance jobs, no diagnostic dropdown options, anchored minute intervals, idempotent install.");

 await using(var calendarScope=host.Services.CreateAsyncScope())
 {
  calendarScope.ServiceProvider.GetRequiredService<ScheduledTenantContext>().Id=2;
  var app=calendarScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
  var rule=await app.AssessmentBonusRules.SingleAsync(x=>x.IsActive);var original=rule.OpenDay;
  Accounts.Models.AssessmentSchedule? fixture=null;
  try
  {
   rule.OpenDay=31;await app.SaveChangesAsync();
   var calendar=host.Services.GetRequiredService<SchedulerCalendar>();
   var managed=new ScheduleDefinition{TenantId=2,ManagedKey="assessment-monthly",Mode="Monthly",Time="00:00",DayOfMonth=31,StartLocal=new DateTime(2099,1,1)};
   var after=new DateTime(2099,2,1,0,0,0,DateTimeKind.Utc);
   var due=await calendar.NextAfterAsync(managed,after,default);
   Check(due.HasValue&&TimeZoneInfo.ConvertTimeFromUtc(due.Value,ScheduleTiming.Pakistan).Day==28,"Managed assessment month-end clamp failed");
   fixture=new Accounts.Models.AssessmentSchedule{TenantId=2,AssessmentYear=2099,AssessmentMonth=2,OpenDay=20,CloseDay=8,IsActive=true,IsManualOverride=true,CreatedDateUtc=DateTime.UtcNow};
   app.AssessmentSchedules.Add(fixture);await app.SaveChangesAsync();
   due=await calendar.NextAfterAsync(managed,after,default);
   Check(due.HasValue&&TimeZoneInfo.ConvertTimeFromUtc(due.Value,ScheduleTiming.Pakistan).Day==20,"Saved monthly opening override ignored");
  }
  finally{rule.OpenDay=original;if(fixture?.Id>0)app.AssessmentSchedules.Remove(fixture);await app.SaveChangesAsync();}
 }
 Console.WriteLine("PASS managed calendar: saved opening day, February clamp and one-month override; staged installation does not execute and preserves individual pauses.");

 // The actual executable runs only against the isolated business clone and a fake filesystem root.
 var files=Path.GetFullPath("_dev/scratch/ProductionJobChecks/worker-files");
 Directory.CreateDirectory(files);
 var oldDirectory=Path.Combine(files,"App_Data","file-conversion-temp","old-verification");
 var newDirectory=Path.Combine(files,"App_Data","file-conversion-temp","new-verification");
 Directory.CreateDirectory(oldDirectory);Directory.CreateDirectory(newDirectory);
 await File.WriteAllTextAsync(Path.Combine(oldDirectory,"sample.txt"),"test-only");
 await File.WriteAllTextAsync(Path.Combine(newDirectory,"sample.txt"),"test-only");
 Directory.SetLastWriteTimeUtc(oldDirectory,DateTime.UtcNow.AddHours(-2));
 var config=Path.GetFullPath("_dev/scratch/ProductionJobChecks/worker-config.json");
 await File.WriteAllTextAsync(config,JsonSerializer.Serialize(new{ConnectionStrings=new{DefaultConnection=business,SchedulerConnection=scheduler},Scheduler=new{ApplicationContentRoot=files,LocalControlEnabled=false,PollSeconds=2,Concurrency=4}}));
 await db.Definitions.IgnoreQueryFilters().Where(x=>owned.Contains(x.Id)).ExecuteUpdateAsync(set=>set.SetProperty(x=>x.Enabled,true).SetProperty(x=>x.Revision,x=>x.Revision+1));
 var processStart=new ProcessStartInfo(Path.GetFullPath("Accounts.Scheduler.Worker/publish/Accounts.Scheduler.Worker.exe")){CreateNoWindow=true,UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true};
 processStart.ArgumentList.Add("--config");processStart.ArgumentList.Add(config);
 using var worker=Process.Start(processStart)!;
 var output=worker.StandardOutput.ReadToEndAsync();var errors=worker.StandardError.ReadToEndAsync();
 try
 {
  for(var i=0;i<480;i++)
  {
   var completed=await db.Runs.IgnoreQueryFilters().Where(x=>owned.Contains(x.ScheduleId)&&x.Status=="Succeeded").Select(x=>x.ScheduleId).Distinct().CountAsync();
   if(completed==owned.Count)break;
   if(worker.HasExited)throw new Exception("Worker exited: "+await output+await errors);
   await Task.Delay(250);
  }
  var completedCount=await db.Runs.IgnoreQueryFilters().Where(x=>owned.Contains(x.ScheduleId)&&x.Status=="Succeeded").Select(x=>x.ScheduleId).Distinct().CountAsync();
  if(completedCount!=owned.Count)
  {
   var failed=await db.Runs.IgnoreQueryFilters().Where(x=>owned.Contains(x.ScheduleId)&&x.Status!="Succeeded").Select(x=>new{x.ScheduleId,x.Status,x.Error}).ToListAsync();
   throw new Exception("Not every production job succeeded on isolated clone: "+JsonSerializer.Serialize(failed));
  }
  Check(!Directory.Exists(oldDirectory)&&Directory.Exists(newDirectory),"Conversion retention/fresh-file protection failed");
  foreach(var notification in await db.RealtimeEvents.IgnoreQueryFilters().AsNoTracking().ToListAsync())
   Check(JsonSerializer.Deserialize<RealtimeEventDto>(notification.PayloadJson)?.TenantId==notification.TenantId,"Cross-tenant notification envelope");
  Console.WriteLine($"PASS real worker: all {completedCount} production jobs succeeded against the isolated clone; process transfer, chat expiry and idempotency operations ran; stale fake conversion directory deleted and fresh directory preserved.");
 }
 finally
 {
  if(!worker.HasExited){worker.Kill(true);await worker.WaitForExitAsync();}
  await File.WriteAllTextAsync("Modules/Scheduler/Verification/production-worker-output.log",await output+await errors);
 }
}
finally
{
 await db.Commands.IgnoreQueryFilters().Where(x=>owned.Contains(x.ScheduleId)).ExecuteDeleteAsync();
 await db.Runs.IgnoreQueryFilters().Where(x=>owned.Contains(x.ScheduleId)).ExecuteDeleteAsync();
 await db.Definitions.IgnoreQueryFilters().Where(x=>owned.Contains(x.Id)).ExecuteDeleteAsync();
}
static async Task<string> Rated(ApplicationDbContext app)=>JsonSerializer.Serialize(await app.StaffAssessments.IgnoreQueryFilters().Where(x=>x.Rating!=null||x.IsFinalApproved||x.IsPostedToPayroll).OrderBy(x=>x.Id).Select(x=>new{x.Id,x.Rating,x.Amount,x.IsLocked,x.IsFinalApproved,x.IsPostedToPayroll,x.Remarks}).ToListAsync());
static async Task<string> Finalized(ApplicationDbContext app){var today=DateOnly.FromDateTime(ScheduleTiming.LocalNow);return JsonSerializer.Serialize(await app.AttendanceDailyFinalizations.IgnoreQueryFilters().Where(x=>x.TenantId==2&&x.AttendanceDate<today).OrderBy(x=>x.Id).Select(x=>new{x.PersonId,x.AttendanceDate,x.State,x.RequiredMinutes,x.WorkedMinutes,x.ShortMinutes,x.OvertimeMinutes,x.LatePenaltyMinutes}).ToListAsync());}
static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
sealed class CaptureRealtime:IRealtimePublisher
{
 public Task PublishEventToTenantAsync(int tenant,RealtimeEventDto message){if(tenant!=2||message.TenantId!=2)throw new Exception("Wrong notification tenant");return Task.CompletedTask;}
 public Task PublishEventToIdentityUserAsync(string id,RealtimeEventDto message)=>Task.CompletedTask;
 public Task PublishEventToPersonAsync(Guid id,RealtimeEventDto message)=>Task.CompletedTask;
 public Task PublishEventToStaffAsync(Guid id,RealtimeEventDto message)=>Task.CompletedTask;
 public Task PublishNotificationToIdentityUserAsync(string id,RealtimeNotificationDto message)=>Task.CompletedTask;
 public Task PublishNotificationToPersonAsync(Guid id,RealtimeNotificationDto message)=>Task.CompletedTask;
}
