using Accounts.Data;
using Accounts.Services.Scheduler;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

// Read-only operator audit of scheduler metadata. No business database connection or handler invocation.
await using var db=new SchedulerDbContext(new DbContextOptionsBuilder<SchedulerDbContext>().UseSqlServer(
 "Server=(localdb)\\MSSQLLocalDB;Database=AccountsScheduler;Trusted_Connection=True;TrustServerCertificate=True").Options,
 new SchedulerTenant(new HttpContextAccessor()));
var definitions=await db.Definitions.IgnoreQueryFilters().AsNoTracking().OrderBy(x=>x.TenantId).ThenBy(x=>x.Name).ToListAsync();
var complete=0;
foreach(var job in definitions)
{
 var last=await db.Runs.IgnoreQueryFilters().AsNoTracking().Where(x=>x.ScheduleId==job.Id).OrderByDescending(x=>x.Id).FirstOrDefaultAsync();
 if(last?.Status=="Succeeded")complete++;
 Console.WriteLine($"Tenant={job.TenantId} | {job.Name} | {job.Mode} | Enabled={job.Enabled} | Next UTC={job.NextRunUtc:O} | Status={last?.Status??"Not run"} | {last?.Result??last?.Error}");
}
Console.WriteLine($"SUMMARY definitions={definitions.Count}; succeeded={complete}; legacy test definitions={definitions.Count(x=>x.ManagedKey==null&&(x.Handler=="System.Health"||x.Handler=="Attendance.Check"||x.Handler=="Assessment.Check"))}");
Console.WriteLine("Pending realtime events="+await db.RealtimeEvents.IgnoreQueryFilters().CountAsync(x=>x.DeliveredUtc==null));
